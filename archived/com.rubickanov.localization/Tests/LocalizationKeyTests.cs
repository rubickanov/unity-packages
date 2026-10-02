using System;
using NUnit.Framework;

namespace Rubickanov.Localization.Tests
{
    [TestFixture]
    public class LocalizationKeyTests
    {
        [Test]
        public void Constructor_ValidArguments_SetsProperties()
        {
            var key = new LocalizationKey("UI", "menu.title");

            Assert.AreEqual("UI", key.Table);
            Assert.AreEqual("menu.title", key.Key);
            Assert.IsTrue(key.IsValid);
        }

        [Test]
        public void Constructor_NullTable_Throws()
        {
            Assert.Throws<ArgumentException>(() => new LocalizationKey(null!, "key"));
        }

        [Test]
        public void Constructor_NullKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new LocalizationKey("UI", null!));
        }

        [Test]
        public void Constructor_EmptyTable_Throws()
        {
            Assert.Throws<ArgumentException>(() => new LocalizationKey("", "key"));
        }

        [Test]
        public void Constructor_EmptyKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new LocalizationKey("UI", ""));
        }

        [Test]
        public void Constructor_WhitespaceTable_Throws()
        {
            Assert.Throws<ArgumentException>(() => new LocalizationKey("   ", "key"));
        }

        [Test]
        public void Constructor_WhitespaceKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new LocalizationKey("UI", "\t"));
        }

        [Test]
        public void Default_IsNotValid()
        {
            var key = default(LocalizationKey);

            Assert.IsFalse(key.IsValid);
        }

        [Test]
        public void Equals_SameValues_ReturnsTrue()
        {
            var a = new LocalizationKey("UI", "title");
            var b = new LocalizationKey("UI", "title");

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
        }

        [Test]
        public void Equals_DifferentTable_ReturnsFalse()
        {
            var a = new LocalizationKey("UI", "title");
            var b = new LocalizationKey("Items", "title");

            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equals_DifferentKey_ReturnsFalse()
        {
            var a = new LocalizationKey("UI", "title");
            var b = new LocalizationKey("UI", "subtitle");

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void GetHashCode_SameValues_SameHash()
        {
            var a = new LocalizationKey("UI", "title");
            var b = new LocalizationKey("UI", "title");

            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void ToString_FormatsAsTableSlashKey()
        {
            var key = new LocalizationKey("UI", "menu.title");

            Assert.AreEqual("UI/menu.title", key.ToString());
        }
    }
}
