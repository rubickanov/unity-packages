using NUnit.Framework;

namespace Rubickanov.Localization.Tests
{
    [TestFixture]
    public class NullLocalizationServiceTests
    {
        [Test]
        public void GetString_ReturnsEmpty()
        {
            using var service = new NullLocalizationService();

            Assert.AreEqual(string.Empty, service.GetString(new LocalizationKey("UI", "title")));
            Assert.AreEqual(string.Empty, service.GetString(new LocalizationKey("UI", "title"), "arg"));
        }

        [Test]
        public void GetAvailableLocales_ReturnsEmptyArray()
        {
            using var service = new NullLocalizationService();

            var locales = service.GetAvailableLocales();

            Assert.IsNotNull(locales);
            Assert.AreEqual(0, locales.Length);
        }

        [Test]
        public void CurrentLocale_IsEmpty()
        {
            using var service = new NullLocalizationService();

            Assert.IsTrue(service.CurrentLocale.CurrentValue.IsEmpty);
        }

        [Test]
        public void IsRTL_IsFalse()
        {
            using var service = new NullLocalizationService();

            Assert.IsFalse(service.IsRTL.CurrentValue);
        }

        [Test]
        public void InitializeAsync_CompletesImmediately()
        {
            using var service = new NullLocalizationService();

            var task = service.InitializeAsync();

            Assert.IsTrue(task.GetAwaiter().IsCompleted);
        }

        [Test]
        public void SetLocaleAsync_ByCode_CompletesImmediately()
        {
            using var service = new NullLocalizationService();

            var task = service.SetLocaleAsync("en");

            Assert.IsTrue(task.GetAwaiter().IsCompleted);
        }

        [Test]
        public void SetLocaleAsync_ByLangLocale_CompletesImmediately()
        {
            using var service = new NullLocalizationService();

            var task = service.SetLocaleAsync(new LangLocale("en"));

            Assert.IsTrue(task.GetAwaiter().IsCompleted);
        }

        [Test]
        public void Dispose_Idempotent()
        {
            var service = new NullLocalizationService();
            service.Dispose();

            Assert.DoesNotThrow(() => service.Dispose());
        }
    }
}
