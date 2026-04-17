using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class TokenizeTests
    {
        [Test]
        public void Tokenize_SimpleSpaceSeparated_ReturnsTokens()
        {
            var tokens = CommandRegistry.Tokenize("give SwordOfFire 5");

            CollectionAssert.AreEqual(new[] { "give", "SwordOfFire", "5" }, tokens);
        }

        [Test]
        public void Tokenize_EmptyString_ReturnsEmpty()
        {
            var tokens = CommandRegistry.Tokenize("");

            Assert.AreEqual(0, tokens.Length);
        }

        [Test]
        public void Tokenize_WhitespaceOnly_ReturnsEmpty()
        {
            var tokens = CommandRegistry.Tokenize("    ");

            Assert.AreEqual(0, tokens.Length);
        }

        [Test]
        public void Tokenize_QuotedStringWithSpaces_TreatedAsSingleToken()
        {
            var tokens = CommandRegistry.Tokenize("say \"hello world\"");

            CollectionAssert.AreEqual(new[] { "say", "hello world" }, tokens);
        }

        [Test]
        public void Tokenize_MultipleSpaces_CollapseToSingleSeparator()
        {
            var tokens = CommandRegistry.Tokenize("a    b     c");

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, tokens);
        }

        [Test]
        public void Tokenize_UnclosedQuote_ConsumesRestAsOneToken()
        {
            var tokens = CommandRegistry.Tokenize("say \"unterminated");

            CollectionAssert.AreEqual(new[] { "say", "unterminated" }, tokens);
        }

        [Test]
        public void Tokenize_LeadingAndTrailingSpaces_AreIgnored()
        {
            var tokens = CommandRegistry.Tokenize("  hello world  ");

            CollectionAssert.AreEqual(new[] { "hello", "world" }, tokens);
        }
    }
}
