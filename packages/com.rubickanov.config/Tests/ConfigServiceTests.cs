using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Config.Tests
{
    [TestFixture]
    public class ConfigServiceTests
    {
        private ConfigService _service;
        private readonly List<ConfigBase> _createdConfigs = new();

        [SetUp]
        public void SetUp()
        {
            _service = new ConfigService(NullLoggerFactory.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();

            foreach (var so in _createdConfigs)
                UnityEngine.Object.DestroyImmediate(so);
            _createdConfigs.Clear();
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

        [Test]
        public void ReleaseAll_EmptyCache_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.ReleaseAll());
        }

        [Test]
        public void Dispose_EmptyCache_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.Dispose());
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            _service.Dispose();
            Assert.DoesNotThrow(() => _service.Dispose());
        }

        [Test]
        public void Get_AfterSeededCache_ReturnsCachedInstance()
        {
            var config = CreateTestConfig();
            SeedCache(config);

            var result = _service.Get<TestConfig>();

            Assert.AreSame(config, result);
        }

        [Test]
        public async Task LoadAsync_AlreadyCached_ReturnsCachedInstanceWithoutTouchingAddressables()
        {
            var config = CreateTestConfig();
            SeedCache(config);

            // If this test reaches the Addressables load path it will either
            // throw or hang — reaching the early-return branch in LoadAsync
            // is the whole point of the test.
            var result = await _service.LoadAsync<TestConfig>();

            Assert.AreSame(config, result);
        }

        [Test]
        public void ReleaseAll_AfterSeededCache_GetThrowsAfterward()
        {
            SeedCache(CreateTestConfig());

            _service.ReleaseAll();

            Assert.Throws<InvalidOperationException>(() => _service.Get<TestConfig>());
        }

        [Test]
        public void Dispose_AfterSeededCache_GetThrowsAfterward()
        {
            SeedCache(CreateTestConfig());

            _service.Dispose();

            Assert.Throws<InvalidOperationException>(() => _service.Get<TestConfig>());
        }

        private TestConfig CreateTestConfig()
        {
            var so = ScriptableObject.CreateInstance<TestConfig>();
            _createdConfigs.Add(so);
            return so;
        }

        /// <summary>
        /// Injects a <see cref="ConfigBase"/> directly into <see cref="ConfigService"/>'s
        /// private cache via reflection. Uses a default (invalid) AsyncOperationHandle so
        /// ReleaseAll() skips the real Addressables.Release call — no Addressables runtime needed.
        /// </summary>
        private void SeedCache<T>(T config) where T : ConfigBase
        {
            var cacheField = typeof(ConfigService).GetField(
                "_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var cacheDict = (IDictionary)cacheField.GetValue(_service)!;

            var cachedConfigType = typeof(ConfigService)
                .GetNestedType("CachedConfig", BindingFlags.NonPublic)!;
            var ctor = cachedConfigType.GetConstructors()[0];
            var handleType = ctor.GetParameters()[1].ParameterType;
            var defaultHandle = Activator.CreateInstance(handleType);
            var cached = ctor.Invoke(new[] { (object)config, defaultHandle! });

            cacheDict[typeof(T)] = cached;
        }

        [RegisterConfig("Test/TestConfig")]
        private class TestConfig : ConfigBase { }

        private class ConfigWithoutAttribute : ConfigBase { }
    }
}
