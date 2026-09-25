using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class ChainTests
    {
        private CommandRegistry _registry = null!;
        private List<string> _ran = null!;
        private string _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            _registry = new CommandRegistry();
            _ran = new List<string>();
            _registry.Register("a", _ => { _ran.Add("a"); return "from a"; });
            _registry.Register("b", args => { _ran.Add("b " + string.Join(",", args)); return "from b"; });
            _registry.Register("bad", _ => throw new CommandException("bad failed"));
        }

        [TearDown]
        public void TearDown() => TestConfig.Release(_config);

        [Test]
        public void Chain_RunsEveryCommandAndReturnsTheLastResult()
        {
            var result = _registry.Execute("a; b x");

            CollectionAssert.AreEqual(new[] { "a", "b x" }, _ran);
            Assert.AreEqual("from b", result.Message);
        }

        [Test]
        public void Chain_FailureInTheMiddle_DoesNotStopTheRest()
        {
            var result = _registry.Execute("bad; a");

            CollectionAssert.AreEqual(new[] { "a" }, _ran);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public void Chain_SemicolonInQuotes_DoesNotSplit()
        {
            _registry.Execute("b \"x;y\"");

            CollectionAssert.AreEqual(new[] { "b x;y" }, _ran);
        }

        [Test]
        public void Chain_EmptyParts_AreSkipped()
        {
            _registry.Execute(";a;; ;");

            CollectionAssert.AreEqual(new[] { "a" }, _ran);
        }

        [Test]
        public void Alias_ToChain_RunsEveryCommand()
        {
            AliasRegistry.Instance.Set("both", "a; b");

            _registry.Execute("both z");

            CollectionAssert.AreEqual(new[] { "a", "b z" }, _ran);
        }

        [TestCase("b $2 $1", "al x y", "b y,x")]
        [TestCase("b [$*]", "al x \"y z\"", "b [x,y z]")]
        [TestCase("b $1-$3", "al x", "b x-")]
        [TestCase("b $x $", "al q", "b $x,$,q")]
        [TestCase("b", "al \"x y\" z", "b x y,z")]
        public void Alias_Placeholders_TakeTheCallArguments(string alias, string call, string ran)
        {
            AliasRegistry.Instance.Set("al", alias);

            _registry.Execute(call);

            CollectionAssert.AreEqual(new[] { ran }, _ran);
        }

        [Test]
        public void GetSuggestions_AfterSemicolon_CompletesTheLastCommand()
        {
            var results = new List<string>();

            _registry.GetSuggestions("a; b", results);

            CollectionAssert.AreEqual(new[] { "b", "bad" }, results);
        }

        [Test]
        public void GetSuggestions_AliasNames_FollowCommands()
        {
            AliasRegistry.Instance.Set("bee", "b");
            var results = new List<string>();

            _registry.GetSuggestions("b", results);

            CollectionAssert.AreEqual(new[] { "b", "bad", "bee" }, results);
        }

        [Test]
        public void GetSuggestions_AfterAliasName_SuggestsTheTargetCommandArguments()
        {
            _registry.Register("pick", _ => null, argProviders: new IAutoCompleteProvider?[] { new StaticListProvider("one", "two") });
            AliasRegistry.Instance.Set("p", "pick");
            var results = new List<string>();

            _registry.GetSuggestions("p t", results);

            CollectionAssert.AreEqual(new[] { "two" }, results);
        }

        [Test]
        public void AliasName_WithReservedCharacters_IsRejected()
        {
            Assert.IsFalse(AliasRegistry.IsValidName("a b"));
            Assert.IsFalse(AliasRegistry.IsValidName("a;b"));
            Assert.IsFalse(AliasRegistry.IsValidName("$a"));
            Assert.IsTrue(AliasRegistry.IsValidName("tp.home"));
            Assert.Throws<System.ArgumentException>(() => AliasRegistry.Instance.Set("a b", "x"));
        }

        [TestCase("he", "help", "help ")]
        [TestCase("say \"a b\" x", "xyz", "say \"a b\" xyz ")]
        [TestCase("say ", "xyz", "say xyz ")]
        [TestCase("a; he", "help", "a; help ")]
        [TestCase("a;", "help", "a;help ")]
        [TestCase("quality Ve", "Very High", "quality \"Very High\" ")]
        public void ApplySuggestion_ReplacesOnlyTheWordBeingTyped(string input, string suggestion, string expected)
        {
            Assert.AreEqual(expected, CommandRegistry.ApplySuggestion(input, suggestion));
        }
    }
}
