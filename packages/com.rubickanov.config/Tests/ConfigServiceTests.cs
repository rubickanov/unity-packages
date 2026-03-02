using System;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Rubickanov.Config.Tests
{
    [TestFixture]
    public class ConfigServiceTests
    {
        private ConfigService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new ConfigService(NullLoggerFactory.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        [Test]
        public void LoadAsync_NoAttribute_ThrowsInvalidOperationException()
        {
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _service.LoadAsync<ConfigWithoutAttribute>());

            Assert.That(ex!.Message, Does.Contain("[RegisterConfig]"));
        }

        [Test]
        public void Get_NotLoaded_ThrowsInvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _service.Get<TestConfig>());

            Assert.That(ex!.Message, Does.Contain("not loaded"));
        }

        [RegisterConfig("Test/TestConfig")]
        private class TestConfig : ConfigBase { }

        private class ConfigWithoutAttribute : ConfigBase { }
    }
}
