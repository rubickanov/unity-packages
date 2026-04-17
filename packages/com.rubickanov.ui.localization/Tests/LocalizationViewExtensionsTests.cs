using System;
using NUnit.Framework;
using Rubickanov.Localization;
using Rubickanov.UI;

namespace Rubickanov.UI.Localization.Tests
{
    [TestFixture]
    public class LocalizationViewExtensionsTests
    {
        private FakeViewModel _vm = null!;
        private NullLocalizationService _loc = null!;

        [SetUp]
        public void SetUp()
        {
            _vm = new FakeViewModel();
            _loc = new NullLocalizationService();
        }

        [TearDown]
        public void TearDown()
        {
            _vm?.Dispose();
            _loc?.Dispose();
        }

        [Test]
        public void CreateLocalized_NullVm_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => LocalizationViewExtensions.CreateLocalized(null!, _loc, new LocalizationKey("UI", "Ok")));
        }

        [Test]
        public void CreateLocalized_NullService_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _vm.CreateLocalized(null!, new LocalizationKey("UI", "Ok")));
        }

        [Test]
        public void CreateLocalized_DefaultKey_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => _vm.CreateLocalized(_loc, default));
        }

        [Test]
        public void CreateLocalized_ValidKey_ReturnsNonNullValue()
        {
            var value = _vm.CreateLocalized(_loc, new LocalizationKey("UI", "Ok"));

            Assert.IsNotNull(value);
        }

        [Test]
        public void CreateLocalized_ValidKey_ReturnsValueWithServiceString()
        {
            var value = _vm.CreateLocalized(_loc, new LocalizationKey("UI", "Ok"));

            Assert.AreEqual(string.Empty, value.CurrentValue);
        }

        private sealed class FakeViewModel : ViewModelBase { }
    }
}
