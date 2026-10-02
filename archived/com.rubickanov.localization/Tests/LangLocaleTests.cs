using NUnit.Framework;

namespace Rubickanov.Localization.Tests
{
    [TestFixture]
    public class LangLocaleTests
    {
        [Test]
        public void Empty_HasEmptyCode()
        {
            Assert.IsTrue(LangLocale.Empty.IsEmpty);
            Assert.AreEqual(string.Empty, LangLocale.Empty.Code);
        }

        [Test]
        public void Empty_IsSingleton()
        {
            var a = LangLocale.Empty;
            var b = LangLocale.Empty;

            Assert.AreEqual(a, b);
        }

        [Test]
        public void Constructor_NullCode_NormalizedToEmpty()
        {
            var locale = new LangLocale(null!, null!, null!);

            Assert.AreEqual(string.Empty, locale.Code);
            Assert.AreEqual(string.Empty, locale.Name);
            Assert.AreEqual(string.Empty, locale.NativeName);
            Assert.IsTrue(locale.IsEmpty);
        }

        [Test]
        public void IsEmpty_EmptyCode_ReturnsTrue()
        {
            var locale = new LangLocale(string.Empty);

            Assert.IsTrue(locale.IsEmpty);
        }

        [Test]
        public void IsEmpty_NonEmptyCode_ReturnsFalse()
        {
            var locale = new LangLocale("en");

            Assert.IsFalse(locale.IsEmpty);
        }

        [Test]
        public void Equals_SameCodeDifferentCasing_ReturnsTrue()
        {
            var a = new LangLocale("en");
            var b = new LangLocale("EN");

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
        }

        [Test]
        public void Equals_DifferentCode_ReturnsFalse()
        {
            var a = new LangLocale("en");
            var b = new LangLocale("ru");

            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equals_DefaultStruct_EqualsEmpty()
        {
            // default(LangLocale) has Code == null; Empty has Code == "". Both are IsEmpty and
            // share a hash, so they must compare equal — otherwise they diverge in sets/dicts.
            LangLocale defaultLocale = default;

            Assert.AreEqual(LangLocale.Empty, defaultLocale);
            Assert.IsTrue(defaultLocale == LangLocale.Empty);
            Assert.AreEqual(LangLocale.Empty.GetHashCode(), defaultLocale.GetHashCode());
        }

        [Test]
        public void GetHashCode_DifferentCasingSameCode_SameHash()
        {
            var a = new LangLocale("en");
            var b = new LangLocale("EN");

            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void GetNameForCode_KnownCode_ReturnsHardcodedName()
        {
            Assert.AreEqual("English", LangLocale.GetNameForCode("en"));
            Assert.AreEqual("Russian", LangLocale.GetNameForCode("ru"));
        }

        [Test]
        public void GetNativeNameForCode_KnownCode_ReturnsHardcodedNativeName()
        {
            Assert.AreEqual("Русский", LangLocale.GetNativeNameForCode("ru"));
        }

        [Test]
        public void GetNameForCode_RegionSuffix_UsesPrimaryCode()
        {
            // "en-US" → primary "en" → hardcoded "English".
            Assert.AreEqual("English", LangLocale.GetNameForCode("en-US"));
        }

        [Test]
        public void GetNameForCode_UnknownCodeWithCultureInfoMatch_ReturnsCultureName()
        {
            // "el" (Greek) is not in the hardcoded 28; CultureInfo should resolve it.
            var result = LangLocale.GetNameForCode("el");

            Assert.IsFalse(string.IsNullOrEmpty(result));
            Assert.AreNotEqual("EL", result, "Expected CultureInfo resolution, not uppercase fallback");
        }

        [Test]
        public void GetNameForCode_GibberishCode_FallsBackToUppercase()
        {
            var result = LangLocale.GetNameForCode("zz-xx-yy");

            Assert.AreEqual("ZZ-XX-YY", result);
        }

        [Test]
        public void GetNameForCode_EmptyCode_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, LangLocale.GetNameForCode(string.Empty));
        }

        [Test]
        public void HardcodedNames_TakePrecedenceOverCultureInfo()
        {
            // "zh" CultureInfo EnglishName is "Chinese" — but "ko" CultureInfo would give
            // "Korean"; what we care about is that the hardcoded value is used even when
            // CultureInfo could answer. Use "ru" and require "Russian" (exact hardcoded).
            Assert.AreEqual("Russian", LangLocale.GetNameForCode("ru"));
            Assert.AreEqual("Русский", LangLocale.GetNativeNameForCode("ru"));
        }
    }
}
