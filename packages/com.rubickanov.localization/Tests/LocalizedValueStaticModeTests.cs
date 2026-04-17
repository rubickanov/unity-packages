using System;
using NUnit.Framework;

namespace Rubickanov.Localization.Tests
{
    // LocalizedValue has two constructors: the internal "real" one that wires into
    // Unity.Localization, and an internal string-only one used by NullLocalizationService.
    // The Null service is the only public way to obtain a static-mode instance; these tests
    // exercise it through that surface.
    [TestFixture]
    public class LocalizedValueStaticModeTests
    {
        [Test]
        public void NullService_Localize_ReturnsStaticEmptyValue()
        {
            using var service = new NullLocalizationService();
            using var value = service.Localize(new LocalizationKey("UI", "menu.title"));

            Assert.AreEqual(string.Empty, value.CurrentValue);
            Assert.AreEqual(string.Empty, value.Value.CurrentValue);
        }

        [Test]
        public void NullService_SetKey_IsNoOp()
        {
            using var service = new NullLocalizationService();
            using var value = service.Localize(new LocalizationKey("UI", "menu.title"));

            value.SetKey(new LocalizationKey("UI", "other.key"));

            Assert.AreEqual(string.Empty, value.CurrentValue);
        }

        [Test]
        public void NullService_SetArguments_IsNoOp()
        {
            using var service = new NullLocalizationService();
            using var value = service.Localize(new LocalizationKey("UI", "menu.title"));

            value.SetArguments("arg1", 42);

            Assert.AreEqual(string.Empty, value.CurrentValue);
        }

        [Test]
        public void AfterDispose_SetKey_Throws()
        {
            using var service = new NullLocalizationService();
            var value = service.Localize(new LocalizationKey("UI", "menu.title"));
            value.Dispose();

            Assert.Throws<ObjectDisposedException>(() => value.SetKey(new LocalizationKey("UI", "x")));
        }

        [Test]
        public void AfterDispose_SetArguments_Throws()
        {
            using var service = new NullLocalizationService();
            var value = service.Localize(new LocalizationKey("UI", "menu.title"));
            value.Dispose();

            Assert.Throws<ObjectDisposedException>(() => value.SetArguments("x"));
        }

        [Test]
        public void Dispose_Idempotent()
        {
            using var service = new NullLocalizationService();
            var value = service.Localize(new LocalizationKey("UI", "menu.title"));
            value.Dispose();

            Assert.DoesNotThrow(() => value.Dispose());
        }
    }
}
