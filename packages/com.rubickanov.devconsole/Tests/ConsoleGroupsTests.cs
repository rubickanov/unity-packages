using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.DevConsole.Commands;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class ConsoleGroupsTests
    {
        private CommandRegistry _registry = null!;
        private string _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            _registry = new CommandRegistry();
            ConsoleCommands.Register(_registry);
            _registry.Register("echo", _ => null);
        }

        [TearDown]
        public void TearDown() => TestConfig.Release(_config);

        [Test]
        public void AliasSet_TakesTheRestOfTheLineWithoutQuotes()
        {
            var result = _registry.Execute("alias set slow timescale 0.2");

            Assert.IsTrue(result.Success, result.Message);
            AliasRegistry.Instance.TryResolve("slow", out var command);
            Assert.AreEqual("timescale 0.2", command);
        }

        [Test]
        public void AliasSet_QuotedChain_IsStoredWhole()
        {
            _registry.Execute("alias set reset \"timescale 1; clear\"");

            AliasRegistry.Instance.TryResolve("reset", out var command);
            Assert.AreEqual("timescale 1; clear", command);
        }

        [Test]
        public void AliasSet_NameOfACommand_IsRefused()
        {
            var result = _registry.Execute("alias set echo history list");

            Assert.IsFalse(result.Success);
            Assert.IsFalse(AliasRegistry.Instance.TryResolve("echo", out _));
        }

        [Test]
        public void AliasRemove_Unknown_IsAnError()
        {
            Assert.IsFalse(_registry.Execute("alias remove nope").Success);
        }

        [Test]
        public void BindSet_ChordAndRestOfLine_AreStored()
        {
            var result = _registry.Execute("bind set ctrl+F5 echo \"a b\" c");

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(BindingRegistry.Instance.Bindings.TryGetValue(
                new KeyChord(Key.F5, KeyModifiers.Ctrl), out var command));
            Assert.AreEqual("echo \"a b\" c", command);
        }

        [Test]
        public void BindSet_UnknownKey_IsAnError()
        {
            Assert.IsFalse(_registry.Execute("bind set Nope echo").Success);
            Assert.AreEqual(0, BindingRegistry.Instance.Bindings.Count);
        }

        [Test]
        public void BindRemove_RemovesTheChordOnly()
        {
            _registry.Execute("bind set F5 echo");
            _registry.Execute("bind set shift+F5 echo");

            Assert.IsTrue(_registry.Execute("bind remove F5").Success);
            Assert.AreEqual(1, BindingRegistry.Instance.Bindings.Count);
            Assert.IsFalse(_registry.Execute("bind remove F5").Success);
        }

        [Test]
        public void Bindings_SurviveAReload()
        {
            _registry.Execute("bind set alt+K echo");

            var reloaded = (BindingRegistry)System.Activator.CreateInstance(typeof(BindingRegistry), true);

            Assert.IsTrue(reloaded.Bindings.ContainsKey(new KeyChord(Key.K, KeyModifiers.Alt)));
        }

        [Test]
        public void Toggle_CyclesThroughItsValues()
        {
            var seen = new List<string>();
            _registry.Register("set", args => { seen.Add(string.Join(",", args)); return null; });

            for (int i = 0; i < 4; i++) _registry.Execute("toggle set 0 1 \"a b\"");

            CollectionAssert.AreEqual(new[] { "0", "1", "a b", "0" }, seen);
        }

        [Test]
        public void Toggle_FailingCommand_IsAnError()
        {
            Assert.IsFalse(_registry.Execute("toggle nothing 0 1").Success);
        }

        [Test]
        public void Toggle_Suggestions_ComeFromTheToggledCommand()
        {
            _registry.Register("mode", _ => null, argProviders: new IAutoCompleteProvider?[] { new StaticListProvider("on", "off") });
            var results = new List<string>();

            _registry.GetSuggestions("toggle mode o", results);

            CollectionAssert.AreEqual(new[] { "on", "off" }, results);
        }

        [TestCase("F5", Key.F5, KeyModifiers.None, "F5")]
        [TestCase("ctrl+f5", Key.F5, KeyModifiers.Ctrl, "ctrl+F5")]
        [TestCase("Alt+Shift+K", Key.K, KeyModifiers.Shift | KeyModifiers.Alt, "shift+alt+K")]
        [TestCase("control+option+Space", Key.Space, KeyModifiers.Ctrl | KeyModifiers.Alt, "ctrl+alt+Space")]
        public void KeyChord_Parses(string text, Key key, KeyModifiers modifiers, string written)
        {
            Assert.IsTrue(KeyChord.TryParse(text, out var chord));
            Assert.AreEqual(new KeyChord(key, modifiers), chord);
            Assert.AreEqual(written, chord.ToString());
        }

        [TestCase("")]
        [TestCase("5")]
        [TestCase("None")]
        [TestCase("meta+F5")]
        [TestCase("ctrl+")]
        public void KeyChord_RefusesNonsense(string text)
        {
            Assert.IsFalse(KeyChord.TryParse(text, out _));
        }

        [Test]
        public void Suggestions_BindKey_KeepTheModifiers()
        {
            var results = new List<string>();

            _registry.GetSuggestions("bind set ctrl+F1", results);

            CollectionAssert.Contains(results, "ctrl+F1");
            CollectionAssert.Contains(results, "ctrl+F10");
        }

        [Test]
        public void Suggestions_BindCommand_CompleteAsACommand()
        {
            var results = new List<string>();

            _registry.GetSuggestions("bind set F5 hist", results);
            CollectionAssert.AreEqual(new[] { "history" }, results);

            results.Clear();
            _registry.GetSuggestions("bind set F5 history c", results);
            CollectionAssert.AreEqual(new[] { "clear" }, results);
        }

        [Test]
        public void Suggestions_AliasRemove_OffersExistingAliases()
        {
            AliasRegistry.Instance.Set("slow", "echo");
            var results = new List<string>();

            _registry.GetSuggestions("alias remove s", results);

            CollectionAssert.AreEqual(new[] { "slow" }, results);
        }

        [Test]
        public void Usage_SetSubcommand_ShowsItsArguments()
        {
            Assert.AreEqual("bind set <key> <command...>", _registry.Commands["bind set"].GetUsageString());
        }
    }
}
