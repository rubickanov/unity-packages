using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class ArgumentsTests
    {
        private CommandRegistry _registry = null!;
        private string _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            _registry = new CommandRegistry();
            _registry.RegisterTarget(new Commands());
        }

        [TearDown]
        public void TearDown() => TestConfig.Release(_config);

        [Test]
        public void Remainder_SeveralWords_ArriveAsTyped()
        {
            var result = _registry.Execute("say a \"b c\"   d");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("a \"b c\"   d", result.Message);
        }

        [Test]
        public void Remainder_SingleQuotedWord_ArrivesUnquoted()
        {
            var result = _registry.Execute("say \"hello world\"");

            Assert.AreEqual("hello world", result.Message);
        }

        [Test]
        public void Remainder_AfterOtherParameters_TakesOnlyTheRest()
        {
            var result = _registry.Execute("keyed F5 timescale 0.5");

            Assert.AreEqual("F5=timescale 0.5", result.Message);
        }

        [Test]
        public void Remainder_Absent_UsesDefault()
        {
            var result = _registry.Execute("keyed F5");

            Assert.AreEqual("F5=none", result.Message);
        }

        [Test]
        public void Remainder_Usage_ShowsEllipsis()
        {
            StringAssert.Contains("<command...>", _registry.Commands["keyed"].GetUsageString());
        }

        [Test]
        public void TooManyArguments_IsAnError()
        {
            var result = _registry.Execute("pair a b c");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Too many arguments", result.Message ?? "");
        }

        [Test]
        public void TypedSubcommand_TooManyArguments_IsAnError()
        {
            _registry.Group("inv", "", "Test", g => g.Add<int>("add", _ => { }));

            var result = _registry.Execute("inv add 1 2");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Too many arguments", result.Message ?? "");
        }

        [Test]
        public void TypedSubcommand_ParseError_IsAnError()
        {
            _registry.Group("inv", "", "Test", g => g.Add<int>("add", _ => { }));

            Assert.IsFalse(_registry.Execute("inv add x").Success);
        }

        [Test]
        public void Group_UnknownSubcommand_IsAnError()
        {
            _registry.Group("inv", "", "Test", g => g.Add("list", () => { }));

            Assert.IsFalse(_registry.Execute("inv nope").Success);
        }

        [Test]
        public void CommandException_FailsWithItsMessage()
        {
            var result = _registry.Execute("fail");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("nope", result.Message);
        }

        [Test]
        public void Alias_QuotedArgument_StaysOneArgument()
        {
            AliasRegistry.Instance.Set("p", "pair");

            var result = _registry.Execute("p \"a b\" c");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("a b|c", result.Message);
        }

        [Test]
        public void CommandLine_RawTail_KeepsQuotesAndInnerSpacing()
        {
            var line = new CommandLine("bind  F5 echo  \"a b\"");

            Assert.AreEqual("F5 echo  \"a b\"", line.RawTail(1));
            Assert.AreEqual("\"a b\"", line.RawToken(3));
            Assert.AreEqual("", line.RawTail(4));
        }

        [Test]
        public void Nullable_Given_ParsesUnderlyingType()
        {
            Assert.AreEqual("7", _registry.Execute("opt 7").Message);
        }

        [Test]
        public void Nullable_Absent_IsNull()
        {
            Assert.AreEqual("unset", _registry.Execute("opt").Message);
        }

        [Test]
        public void Nullable_Unparsable_IsAnError()
        {
            Assert.IsFalse(_registry.Execute("opt x").Success);
        }

        [Test]
        public void NullAndEmptyDefaults_ShowNoValueInUsage()
        {
            Assert.AreEqual("opt [<value>]", _registry.Commands["opt"].GetUsageString());
            Assert.AreEqual("keyed <key> [<command...>]", _registry.Commands["keyed"].GetUsageString().Replace("=none", ""));
        }

        [Test]
        public void NullableBool_GetsBoolSuggestions()
        {
            var results = new System.Collections.Generic.List<string>();
            _registry.GetSuggestions("flag ", results);

            CollectionAssert.AreEquivalent(new[] { "true", "false" }, results);
        }

        private class Commands
        {
            [ConsoleCommand("opt")]
            public string Opt(int? value = null) => value?.ToString() ?? "unset";

            [ConsoleCommand("flag")]
            public void Flag(bool? on = null) { }

            [ConsoleCommand("say")]
            public string Say([Remainder] string text) => text;

            [ConsoleCommand("keyed")]
            public string Keyed(string key, [Remainder] string command = "none") => $"{key}={command}";

            [ConsoleCommand("pair")]
            public string Pair(string a, string b) => $"{a}|{b}";

            [ConsoleCommand("fail")]
            public void Fail() => throw new CommandException("nope");
        }
    }
}
