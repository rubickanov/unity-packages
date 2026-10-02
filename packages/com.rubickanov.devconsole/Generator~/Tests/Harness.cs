using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Rubickanov.DevConsole.Generator.Tests
{
    /// <summary>
    /// Compiles a game's source with the generator against a stand-in for the console and UnityEngine, the way Unity
    /// compiles an assembly that references the console.
    /// </summary>
    internal static class Harness
    {
        // What generated code calls, shaped like the package's runtime; the registry records instead of running
        private const string ConsoleStub = @"
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    public struct Vector3 { public float x, y, z; }
}

namespace UnityEngine.Scripting
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class AlwaysLinkAssemblyAttribute : Attribute { }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute { }
}

namespace Rubickanov.DevConsole
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ConsoleCommandAttribute : Attribute
    {
        public ConsoleCommandAttribute(string name, string description = """", string category = ""General"") { }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class AutoCompleteAttribute : Attribute
    {
        public AutoCompleteAttribute(int argumentIndex, Type providerType, params string[] providerArgs) { }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class RemainderAttribute : Attribute { }

    public interface IAutoCompleteProvider
    {
        string? Hint => null;
        void GetSuggestions(string partial, List<string> results);
    }

    public class StaticListProvider : IAutoCompleteProvider
    {
        public readonly string[] Options;
        public StaticListProvider(params string[] options) => Options = options;
        public void GetSuggestions(string partial, List<string> results) { }
    }

    public sealed class CommandParameter
    {
        public string Name { get; }
        public Type Type { get; }
        public bool HasDefaultValue { get; }
        public object? DefaultValue { get; }
        public CommandParameter(string name, Type type) { Name = name; Type = type; }
        public CommandParameter(string name, Type type, object? defaultValue)
        {
            Name = name; Type = type; HasDefaultValue = true; DefaultValue = defaultValue;
        }
    }

    public sealed class Added
    {
        public string Name = """", Description = """", Category = """";
        public CommandParameter[] Parameters = Array.Empty<CommandParameter>();
        public IAutoCompleteProvider?[] Providers = Array.Empty<IAutoCompleteProvider?>();
        public bool HasRemainder;
        public object? Target;
        public Func<object?[], object?> Invoke = _ => null;
    }

    public class CommandRegistry
    {
        public readonly List<Added> Commands = new();
        private readonly Dictionary<Type, IAutoCompleteProvider> _shared = new();

        public void AddGenerated(string name, string description, string category, CommandParameter[] parameters,
            IAutoCompleteProvider?[] providers, bool hasRemainder, object? target, Func<object?[], object?> invoke)
        {
            Commands.Add(new Added { Name = name, Description = description, Category = category,
                Parameters = parameters, Providers = providers, HasRemainder = hasRemainder, Target = target,
                Invoke = invoke });
        }

        public IAutoCompleteProvider SharedProvider(Type type, Func<IAutoCompleteProvider> create)
        {
            if (!_shared.TryGetValue(type, out var provider)) _shared[type] = provider = create();
            return provider;
        }
    }

    public static class GeneratedCommands
    {
        public static readonly List<Action<CommandRegistry>> Static = new();
        public static readonly Dictionary<Type, Action<CommandRegistry, object>> Targets = new();
        public static void Add(Action<CommandRegistry> register) { if (!Static.Contains(register)) Static.Add(register); }
        public static void AddTarget(Type type, Action<CommandRegistry, object> bind) => Targets[type] = bind;
    }
}
";

        private static readonly ImmutableArray<MetadataReference> FrameworkReferences =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

        private static readonly Lazy<byte[]> ConsoleImage = new(() => Compile(ConsoleStub));

        private static readonly Lazy<MetadataReference> ConsoleReference =
            new(() => MetadataReference.CreateFromImage(ConsoleImage.Value));

        // The console without UnityEngine, for an assembly that has no engine references
        private static readonly Lazy<MetadataReference> ConsoleOnlyReference = new(() =>
            MetadataReference.CreateFromImage(Compile(ConsoleStub
                .Replace("namespace UnityEngine\n", "namespace NotUnityEngine\n")
                .Replace("namespace UnityEngine.Scripting", "namespace NotUnityEngine.Scripting"))));

        private static byte[] Compile(string source)
        {
            var compilation = CSharpCompilation.Create("Rubickanov.DevConsole.Runtime",
                new[] { Parse(source) }, FrameworkReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            return stream.ToArray();
        }

        public sealed class Result
        {
            public ImmutableArray<Diagnostic> GeneratorDiagnostics;
            public ImmutableArray<Diagnostic> CompileErrors;
            public Dictionary<string, string> Sources = new();
            public Compilation Output = null!;

            public IEnumerable<string> Ids => GeneratorDiagnostics.Select(d => d.Id);

            public string AllSources => string.Join("\n// ----\n", Sources.Values);
        }

        /// <summary>Runs the generator over <paramref name="source"/> in an assembly that references the console.</summary>
        public static Result Run(string source, bool editor = true, bool engine = true)
        {
            var symbols = editor ? new[] { "UNITY_EDITOR" } : Array.Empty<string>();
            var references = FrameworkReferences.Add(ConsoleReference.Value);
            if (!engine) references = references.Remove(ConsoleReference.Value).Add(ConsoleOnlyReference.Value);

            var compilation = CSharpCompilation.Create("Game.Commands", new[] { Parse(source, symbols) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            var driver = CSharpGeneratorDriver.Create(new[] { new CommandGenerator().AsSourceGenerator() },
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var output,
                out var diagnostics);

            var run = driver.GetRunResult();
            return new Result
            {
                GeneratorDiagnostics = run.Diagnostics,
                CompileErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray(),
                Sources = run.GeneratedTrees.ToDictionary(t => Path.GetFileName(t.FilePath), t => t.GetText().ToString()),
                Output = output
            };
        }

        /// <summary>Compiles the generated assembly, loads it, runs its load callbacks and returns the console stub.</summary>
        public static Loaded Load(Result result)
        {
            Assert.IsEmpty(result.CompileErrors, string.Join("\n", result.CompileErrors) + "\n" + result.AllSources);
            using var stream = new MemoryStream();
            var emit = result.Output.Emit(stream);
            Assert.IsTrue(emit.Success, string.Join("\n", emit.Diagnostics));

            var context = new System.Runtime.Loader.AssemblyLoadContext(null, isCollectible: true);
            var console = context.LoadFromStream(new MemoryStream(ConsoleImage.Value));
            context.Resolving += (_, name) => name.Name == "Rubickanov.DevConsole.Runtime" ? console : null;
            var game = context.LoadFromStream(new MemoryStream(stream.ToArray()));

            foreach (var method in game.GetTypes().SelectMany(t =>
                         t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)))
            {
                if (method.GetCustomAttributes().Any(a => a.GetType().Name == "RuntimeInitializeOnLoadMethodAttribute"))
                    method.Invoke(null, null);
            }

            return new Loaded(console, game);
        }

        private static SyntaxTree Parse(string source, params string[] symbols) =>
            CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: symbols));

        /// <summary>The loaded game assembly and the console stub it registered with.</summary>
        public sealed class Loaded
        {
            private readonly Assembly _console;
            public readonly Assembly Game;

            public Loaded(Assembly console, Assembly game)
            {
                _console = console;
                Game = game;
            }

            /// <summary>A fresh registry with the static commands and, for each target, its instance commands.</summary>
            public dynamic Register(params object[] targets)
            {
                var registryType = _console.GetType("Rubickanov.DevConsole.CommandRegistry")!;
                var registry = Activator.CreateInstance(registryType)!;
                var catalog = _console.GetType("Rubickanov.DevConsole.GeneratedCommands")!;
                var statics = (System.Collections.IList)catalog.GetField("Static")!.GetValue(null)!;
                foreach (Delegate register in statics) register.DynamicInvoke(registry);
                var bindings = (System.Collections.IDictionary)catalog.GetField("Targets")!.GetValue(null)!;
                foreach (var target in targets)
                    for (var type = target.GetType(); type != null; type = type.BaseType)
                        if (bindings.Contains(type))
                            ((Delegate)bindings[type]!).DynamicInvoke(registry, target);
                return registry;
            }

            public object Create(string typeName) =>
                Activator.CreateInstance(Game.GetType(typeName, throwOnError: true)!, nonPublic: true)!;
        }
    }
}
