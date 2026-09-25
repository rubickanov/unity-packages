using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    /// <summary>Names of several words: <c>scene load</c> is a subcommand of the <c>scene</c> group.</summary>
    [TestFixture]
    public class CommandPathTests
    {
        private CommandRegistry _registry = null!;
        private readonly List<string> _results = new();

        [SetUp]
        public void SetUp()
        {
            _registry = new CommandRegistry();
            _results.Clear();
        }

        [Test]
        public void Name_IsLowercaseWordsSeparatedByOneSpace()
        {
            _registry.Register("  Scene   Load ", _ => "ok");

            Assert.IsTrue(_registry.Commands.ContainsKey("scene load"));
            Assert.AreEqual("ok", _registry.Execute("SCENE load").Message);
        }

        [Test]
        public void LongestName_Wins_AndTheShorterKeepsItsOwn()
        {
            _registry.Register("scene", args => $"scene {args.Length}");
            _registry.Register("scene load", args => $"load {string.Join(",", args)}");

            Assert.AreEqual("load arena", _registry.Execute("scene load arena").Message);
            Assert.AreEqual("scene 0", _registry.Execute("scene").Message);
            Assert.AreEqual("scene 1", _registry.Execute("scene other").Message);
        }

        [Test]
        public void Group_AddsToCommandsRegisteredElsewhere()
        {
            _registry.Register("log unity", _ => "unity");
            _registry.Group("log", "Logging", "Test", g => g.Add("level", _ => "level"));

            Assert.AreEqual("unity", _registry.Execute("log unity").Message);
            Assert.AreEqual("level", _registry.Execute("log level").Message);
        }

        [Test]
        public void GroupAlone_ListsEverySubcommand()
        {
            _registry.Register("log unity", _ => null, "Copy Unity logs");
            _registry.Group("log", "", "Test", g => g.Add("level", _ => null, "Set a level"));

            var result = _registry.Execute("log");

            Assert.IsTrue(result.Success);
            StringAssert.Contains("unity - Copy Unity logs", result.Message ?? "");
            StringAssert.Contains("level - Set a level", result.Message ?? "");
        }

        [Test]
        public void UnknownSubcommand_NamesTheGroup()
        {
            _registry.Register("net host", _ => null);

            var result = _registry.Execute("net nope");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Unknown subcommand 'nope'", result.Message ?? "");
            StringAssert.Contains("'net'", result.Message ?? "");
        }

        [Test]
        public void NestedGroups_Resolve()
        {
            _registry.Register("net host lan", _ => "lan");

            Assert.AreEqual("lan", _registry.Execute("net host lan").Message);
            StringAssert.Contains("lan", _registry.Execute("net host").Message ?? "");
        }

        [Test]
        public void Remainder_StartsAfterTheWholeName()
        {
            _registry.RegisterTarget(new Service());

            var result = _registry.Execute("say loud hello \"big\" world");

            Assert.AreEqual("hello \"big\" world", result.Message);
        }

        [Test]
        public void TypedAttributeArguments_StartAfterTheWholeName()
        {
            _registry.RegisterTarget(new Service());

            Assert.AreEqual("7", _registry.Execute("math double 3.5").Message);
        }

        [Test]
        public void Suggestions_FirstWord_ListsEachGroupOnce()
        {
            _registry.Register("scene", _ => null);
            _registry.Register("scene load", _ => null);
            _registry.Register("scene list", _ => null);
            _registry.Register("quit", _ => null);

            _registry.GetSuggestions("", _results);

            CollectionAssert.AreEqual(new[] { "quit", "scene" }, _results);
        }

        [Test]
        public void Suggestions_AfterGroup_ListsNextWordsAndTheCommandsArguments()
        {
            _registry.Register("scene", _ => null, "", "Test", new IAutoCompleteProvider?[] { new StaticListProvider("main") });
            _registry.Register("scene load", _ => null);
            _registry.Register("scene list", _ => null);

            _registry.GetSuggestions("scene ", _results);

            CollectionAssert.AreEqual(new[] { "list", "load", "main" }, _results);
        }

        [Test]
        public void Suggestions_SubcommandArguments()
        {
            _registry.Register("scene load", _ => null, "", "Test",
                new IAutoCompleteProvider?[] { new StaticListProvider("arena", "lab") });

            _registry.GetSuggestions("scene load l", _results);

            CollectionAssert.AreEqual(new[] { "lab" }, _results);
        }

        [Test]
        public void DescribeSuggestion_NamesTheSubcommand()
        {
            _registry.Register("scene load", _ => null, "Load a scene");
            _registry.Group("log", "Log channels", "Test", g => g.Add("level", _ => null));

            Assert.AreEqual("Load a scene", _registry.DescribeSuggestion("scene lo", "load"));
            Assert.AreEqual("Log channels", _registry.DescribeSuggestion("l", "log"));
            Assert.IsNull(_registry.DescribeSuggestion("scene load ", "arena"));
        }

        [Test]
        public void Unregister_Group_RemovesItsSubcommands()
        {
            _registry.Group("inv", "", "Test", g => g.Add("add", _ => null).Add("clear", _ => null));

            Assert.IsTrue(_registry.Unregister("inv"));

            Assert.AreEqual(0, _registry.Commands.Count);
            Assert.IsFalse(_registry.Execute("inv add").Success);
        }

        [Test]
        public void Help_Group_ListsItsSubcommands()
        {
            // Initialize also discovers the package's own `log unity`, and runs autoexec from a test config
            var config = TestConfig.Use();
            try
            {
                _registry.Initialize();
                _registry.Group("log", "", "Test", g => g.Add("level", _ => null, "Set a level"));
                ConsoleLog.Clear();

                Assert.IsTrue(_registry.Execute("help log").Success);

                var logged = new List<string>();
                foreach (var entry in ConsoleLog.Entries) logged.Add(entry.Message);
                var text = string.Join("\n", logged);
                StringAssert.Contains("log level", text);
                StringAssert.Contains("log unity", text);
            }
            finally
            {
                TestConfig.Release(config);
            }
        }

        private class Service
        {
            [ConsoleCommand("say loud")]
            public string SayLoud([Remainder] string text) => text;

            [ConsoleCommand("math double")]
            public string Double(float value) => (value * 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
