using System.Linq;
using NUnit.Framework;

namespace Rubickanov.DevConsole.Generator.Tests
{
    [TestFixture]
    public class GeneratorTests
    {
        private const string Usings = "using Rubickanov.DevConsole;\nusing System.Collections.Generic;\n";

        [Test]
        public void Generate_StaticCommand_RegisteredUnderNormalizedNameAndCallable()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal static class Cheats
    {
        public static int Healed;
        [ConsoleCommand(""  Heal   Player "", ""Restore health"", ""Cheats"")]
        public static string Heal(int amount = 100) { Healed += amount; return ""healed "" + amount; }
    }
}");
            Assert.IsEmpty(result.GeneratorDiagnostics);
            var loaded = Harness.Load(result);
            var registry = loaded.Register();

            var command = registry.Commands[0];
            Assert.AreEqual("heal player", command.Name);
            Assert.AreEqual("Restore health", command.Description);
            Assert.AreEqual("Cheats", command.Category);
            Assert.AreEqual(100, command.Parameters[0].DefaultValue);
            Assert.AreEqual("healed 5", command.Invoke(new object?[] { 5 }));
        }

        [Test]
        public void Generate_DefaultsOfEveryKind_KeepTheirTypeAndValue()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    public enum Mode { Off, Slow, Fast }
    internal static class C
    {
        [ConsoleCommand(""d"")]
        public static void D(float f = 1.5f, double d = -0.25, long l = -9223372036854775808, Mode m = Mode.Fast,
            Mode? n = null, int? i = 3, string s = ""a \""q\"""", char c = '\n', bool b = true,
            UnityEngine.Vector3 v = default, float nan = float.NaN, ulong u = 18446744073709551615) { }
    }
}");
            var loaded = Harness.Load(result);
            var parameters = loaded.Register().Commands[0].Parameters;

            object?[] expected =
            {
                1.5f, -0.25, long.MinValue, loaded.Game.GetType("Game.Mode")!.GetEnumValues().GetValue(2), null, 3,
                "a \"q\"", '\n', true, null, float.NaN, ulong.MaxValue
            };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.IsTrue(parameters[i].HasDefaultValue, parameters[i].Name);
                if (i == 9)
                    Assert.AreEqual("Vector3", parameters[i].DefaultValue.GetType().Name);
                else
                    Assert.AreEqual(expected[i], parameters[i].DefaultValue, parameters[i].Name);
            }
        }

        [Test]
        public void Invoke_NullableAndRemainderArguments_ArriveAsTyped()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal static class C
    {
        [ConsoleCommand(""say"")]
        public static string Say(int? times, [Remainder] string text) => (times?.ToString() ?? ""none"") + "":"" + text;
    }
}");
            var command = Harness.Load(result).Register().Commands[0];

            Assert.IsTrue(command.HasRemainder);
            Assert.AreEqual("none:hi there", command.Invoke(new object?[] { null, "hi there" }));
            Assert.AreEqual("2:x", command.Invoke(new object?[] { 2, "x" }));
        }

        [Test]
        public void Bind_DerivedTarget_RegistersBaseTypeCommandsOnTheSameTarget()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal class Base
    {
        public int Count;
        [ConsoleCommand(""count"")]
        public int Increment() => ++Count;
    }
    internal sealed class Service : Base
    {
        [ConsoleCommand(""name"")]
        public string Name() => ""service"";
    }
}");
            var loaded = Harness.Load(result);
            var service = loaded.Create("Game.Service");
            var commands = loaded.Register(service).Commands;

            Assert.AreEqual(2, commands.Count);
            foreach (var command in commands) Assert.AreSame(service, command.Target);
            var count = Enumerable.First(commands, (System.Func<dynamic, bool>)(c => c.Name == "count"));
            Assert.AreEqual(1, count.Invoke(new object?[0]));
            Assert.AreEqual(2, count.Invoke(new object?[0]));
        }

        [Test]
        public void Generate_Providers_SharedWithoutArgumentsMadePerCommandWithThem()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal sealed class Names : IAutoCompleteProvider
    {
        public void GetSuggestions(string partial, List<string> results) { }
    }
    internal static class C
    {
        [ConsoleCommand(""a"")]
        [AutoComplete(0, typeof(Names))]
        [AutoComplete(1, typeof(StaticListProvider), ""x"", ""y"")]
        public static void A(string name, string option) { }

        [ConsoleCommand(""b"")]
        [AutoComplete(0, typeof(Names))]
        public static void B(string name, int count) { }
    }
}");
            var commands = Harness.Load(result).Register().Commands;
            var a = commands[0];
            var b = commands[1];

            Assert.AreSame(a.Providers[0], b.Providers[0]);
            Assert.AreEqual(new[] { "x", "y" }, a.Providers[1].Options);
            Assert.IsNull(b.Providers[1]);
        }

        [Test]
        public void Generate_PrivateCommandInPartialTypes_WrittenInsideTheType()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    public partial class Fixture
    {
        private partial class Hidden
        {
            [ConsoleCommand(""hidden"")]
            private string Run(string value) => ""got "" + value;

            [ConsoleCommand(""hidden static"")]
            private static string Static() => ""static"";
        }
    }
}");
            Assert.IsEmpty(result.GeneratorDiagnostics);
            Assert.IsTrue(result.Sources.Keys.Any(k => k.Contains("Game_Fixture_Hidden")), string.Join(", ", result.Sources.Keys));
            var loaded = Harness.Load(result);
            var hidden = loaded.Create("Game.Fixture+Hidden");
            var commands = loaded.Register(hidden).Commands;

            Assert.AreEqual(2, commands.Count);
            Assert.AreEqual("static", commands[0].Invoke(new object?[0]));
            Assert.AreEqual("got x", commands[1].Invoke(new object?[] { "x" }));
        }

        [Test]
        public void Generate_PrivateCommandNotPartial_ErrorNamesTypesToMakePartial()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    public class Fixture
    {
        private class Hidden
        {
            [ConsoleCommand(""hidden"")]
            public void Run() { }
        }
    }
}");
            var error = result.GeneratorDiagnostics.Single();
            Assert.AreEqual("DEVCON004", error.Id);
            StringAssert.Contains("Fixture and Hidden partial", error.GetMessage());
            Assert.IsEmpty(result.CompileErrors);
        }

        [Test]
        public void Generate_ProviderWithoutFittingConstructor_Error()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal sealed class Names : IAutoCompleteProvider
    {
        public Names() { }
        public void GetSuggestions(string partial, List<string> results) { }
    }
    internal static class C
    {
        [ConsoleCommand(""a"")]
        [AutoComplete(0, typeof(Names), ""an argument it cannot take"")]
        public static void A(string name) { }
    }
}");
            Assert.AreEqual(new[] { "DEVCON007" }, result.Ids.ToArray());
        }

        [Test]
        public void Generate_MistakesFormerlyFoundAtRuntime_AreCompileErrors()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal static class C
    {
        [ConsoleCommand("" "")]
        public static void Empty() { }

        [ConsoleCommand(""index"")]
        [AutoComplete(1, typeof(StaticListProvider))]
        public static void Index(string one) { }

        [ConsoleCommand(""not a provider"")]
        [AutoComplete(0, typeof(string))]
        public static void NotProvider(string one) { }

        [ConsoleCommand(""remainder"")]
        public static void Remainder([Remainder] string first, int second) { }

        [ConsoleCommand(""by ref"")]
        public static void ByRef(ref int value) { }

        [ConsoleCommand(""generic"")]
        public static void Generic<T>(T value) { }
    }

    internal struct S
    {
        [ConsoleCommand(""struct"")]
        public void Instance() { }
    }
}");
            CollectionAssert.AreEquivalent(
                new[] { "DEVCON001", "DEVCON005", "DEVCON006", "DEVCON008", "DEVCON003", "DEVCON002", "DEVCON009" },
                result.Ids.ToArray());
            Assert.IsEmpty(result.CompileErrors);
        }

        [Test]
        public void Generate_DuplicateStaticNames_WarningAndBothRegister()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal static class C
    {
        [ConsoleCommand(""same"")] public static void A() { }
        [ConsoleCommand(""Same"")] public static void B() { }
    }
}");
            Assert.AreEqual(new[] { "DEVCON010" }, result.Ids.ToArray());
            Assert.AreEqual(2, Harness.Load(result).Register().Commands.Count);
        }

        [Test]
        public void Generate_AssemblyWithCommands_MarkedAlwaysLinkUnlessAlreadyMarked()
        {
            const string command = @"
namespace Game { internal static class C { [ConsoleCommand(""a"")] public static void A() { } } }";

            var plain = Harness.Run(Usings + command);
            StringAssert.Contains("[assembly: global::UnityEngine.Scripting.AlwaysLinkAssembly]", plain.AllSources);

            var marked = Harness.Run(Usings + "[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]\n" + command);
            Assert.IsEmpty(marked.CompileErrors);
            StringAssert.DoesNotContain("AlwaysLinkAssembly", marked.AllSources);
        }

        [Test]
        public void Generate_AssemblyWithoutCommands_WritesNothing()
        {
            var result = Harness.Run(Usings + "namespace Game { internal static class C { public static void A() { } } }");

            Assert.IsEmpty(result.Sources);
        }

        [Test]
        public void Generate_PlayerBuildWithoutEditor_Compiles()
        {
            var result = Harness.Run(Usings + @"
namespace Game
{
    internal static class C { [ConsoleCommand(""a"")] public static void A() { } }
    public partial class F { private partial class H { [ConsoleCommand(""h"")] private void B() { } } }
}", editor: false);

            Assert.IsEmpty(result.GeneratorDiagnostics);
            Assert.IsEmpty(result.CompileErrors, string.Join("\n", result.CompileErrors));
        }

        [Test]
        public void Generate_AssemblyWithoutEngineReferences_Error()
        {
            var result = Harness.Run(Usings + @"
namespace Game { internal static class C { [ConsoleCommand(""a"")] public static void A() { } } }", engine: false);

            Assert.AreEqual(new[] { "DEVCON011" }, result.Ids.ToArray());
            Assert.IsEmpty(result.Sources);
        }

        [Test]
        public void Generate_KeywordNamesAndClashingNamespace_Compile()
        {
            var result = Harness.Run(Usings + @"
namespace Game.Rubickanov
{
    internal static class @class
    {
        [ConsoleCommand(""kw"")]
        public static void @event(int @int, string r = ""r"", string a = ""a"", string t = ""t"") { }
    }
}");
            Assert.IsEmpty(result.GeneratorDiagnostics);
            Harness.Load(result);
        }
    }
}
