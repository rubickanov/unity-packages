using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.Codegen.Editor;

namespace Rubickanov.Codegen.Tests
{
    [TestFixture]
    public class IdentifierSanitizerTests
    {
        [Test]
        public void Sanitize_InvalidCharacters_BecomeSeparatorsAndPascalCase()
        {
            var result = IdentifierSanitizer.Sanitize("my-key", lowercaseRemainder: true);

            Assert.AreEqual("MyKey", result);
        }

        [Test]
        public void Sanitize_LeadingDigit_PrefixedWithUnderscore()
        {
            var result = IdentifierSanitizer.Sanitize("2nd-place", lowercaseRemainder: true);

            Assert.AreEqual("_2ndPlace", result);
        }

        [Test]
        public void Sanitize_CSharpKeyword_EscapedWithAt()
        {
            var result = IdentifierSanitizer.Sanitize("class", lowercaseRemainder: true);

            Assert.AreEqual("@Class", result);
        }

        [Test]
        public void Sanitize_LowercaseRemainderTrue_LowercasesInteriorCharacters()
        {
            var result = IdentifierSanitizer.Sanitize("DoT", lowercaseRemainder: true);

            Assert.AreEqual("Dot", result);
        }

        [Test]
        public void Sanitize_LowercaseRemainderFalse_PreservesInteriorCharacters()
        {
            var result = IdentifierSanitizer.Sanitize("DoT", lowercaseRemainder: false);

            Assert.AreEqual("DoT", result);
        }

        [Test]
        public void Sanitize_EmptyInput_ReturnsUnderscore()
        {
            var result = IdentifierSanitizer.Sanitize("", lowercaseRemainder: false);

            Assert.AreEqual("_", result);
        }

        [Test]
        public void Sanitize_AllInvalidCharacters_ReturnsUnderscore()
        {
            var result = IdentifierSanitizer.Sanitize("!!!", lowercaseRemainder: false);

            Assert.AreEqual("_", result);
        }

        [Test]
        public void MakeUnique_FirstUse_ReturnsNameUnchanged()
        {
            var used = new HashSet<string>();

            var result = IdentifierSanitizer.MakeUnique("Fire", used);

            Assert.AreEqual("Fire", result);
        }

        [Test]
        public void MakeUnique_Collision_AppendsNumericSuffix()
        {
            var used = new HashSet<string> { "Fire" };

            var result = IdentifierSanitizer.MakeUnique("Fire", used);

            Assert.AreEqual("Fire_2", result);
        }

        [Test]
        public void MakeUnique_MultipleCollisions_IncrementsSuffix()
        {
            var used = new HashSet<string>();

            IdentifierSanitizer.MakeUnique("Fire", used);
            IdentifierSanitizer.MakeUnique("Fire", used);
            var third = IdentifierSanitizer.MakeUnique("Fire", used);

            Assert.AreEqual("Fire_3", third);
        }

        [Test]
        public void MakeUnique_KeywordEscape_PreservedOnSuffixedForm()
        {
            var used = new HashSet<string> { "@class" };

            var result = IdentifierSanitizer.MakeUnique("@class", used);

            Assert.AreEqual("@class_2", result);
        }
    }
}
