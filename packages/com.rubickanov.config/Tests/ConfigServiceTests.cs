using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Config.Tests
{
    [TestFixture]
    public class ConfigServiceTests
    {
        private FakeAssetLoader _loader;
        private ConfigService _service;
        private readonly List<ConfigBase> _createdConfigs = new();

        [SetUp]
        public void SetUp()
        {
            _loader = new FakeAssetLoader();
            _service = new ConfigService(NullLoggerFactory.Instance, _loader);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();

            foreach (var so in _createdConfigs)
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
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
            var ex = Assert.Throws<InvalidOperationException>(() => _service.Get<TestConfig>());

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
        public async Task LoadAsync_HappyPath_ReturnsAssetFromLoader()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);

            var result = await _service.LoadAsync<TestConfig>();

            Assert.AreSame(config, result);
            Assert.AreEqual(1, _loader.LoadCalls);
        }

        [Test]
        public async Task LoadAsync_CalledTwice_LoadsOnceAndCaches()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);

            var first = await _service.LoadAsync<TestConfig>();
            var second = await _service.LoadAsync<TestConfig>();

            Assert.AreSame(first, second);
            Assert.AreEqual(1, _loader.LoadCalls);
        }

        [Test]
        public async Task Get_AfterLoadAsync_ReturnsLoadedInstance()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);
            await _service.LoadAsync<TestConfig>();

            var result = _service.Get<TestConfig>();

            Assert.AreSame(config, result);
        }

        [Test]
        public void LoadAsync_LoaderThrows_PropagatesException()
        {
            _loader.ThrowOnLoad = new InvalidOperationException("boom");

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _service.LoadAsync<TestConfig>());

            Assert.AreEqual("boom", ex!.Message);
        }

        [Test]
        public void LoadAsync_ValidationFails_ThrowsAndReleasesHandle()
        {
            var config = CreateConfig<InvalidatingConfig>();
            config.ValidateResult = false;
            _loader.Register("Test/InvalidatingConfig", config);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _service.LoadAsync<InvalidatingConfig>());

            Assert.That(ex!.Message, Does.Contain("Validate()"));
            Assert.AreEqual(1, _loader.ReleaseCalls);
        }

        [Test]
        public async Task LoadAsync_ValidationFails_DoesNotCache()
        {
            var config = CreateConfig<InvalidatingConfig>();
            config.ValidateResult = false;
            _loader.Register("Test/InvalidatingConfig", config);

            try
            {
                await _service.LoadAsync<InvalidatingConfig>();
            }
            catch (InvalidOperationException) { }

            Assert.Throws<InvalidOperationException>(() => _service.Get<InvalidatingConfig>());
        }

        [Test]
        public async Task LoadAsync_ConcurrentSameType_CallsLoaderOnce()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);
            _loader.LoadGate = new UniTaskCompletionSource();

            var first = _service.LoadAsync<TestConfig>();
            var second = _service.LoadAsync<TestConfig>();

            _loader.LoadGate.TrySetResult();
            var results = await UniTask.WhenAll(first, second);

            Assert.AreSame(config, results.Item1);
            Assert.AreSame(config, results.Item2);
            Assert.AreEqual(1, _loader.LoadCalls);
        }

        [Test]
        public async Task LoadAsync_AfterReleaseAll_ReloadsFromLoader()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);
            await _service.LoadAsync<TestConfig>();

            _service.ReleaseAll();
            await _service.LoadAsync<TestConfig>();

            Assert.AreEqual(2, _loader.LoadCalls);
            Assert.AreEqual(1, _loader.ReleaseCalls);
        }

        [Test]
        public void LoadAsync_CancellationRequested_Throws()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await _service.LoadAsync<TestConfig>(cts.Token));
        }

        [Test]
        public async Task RefreshCatalogIfNeededAsync_DelegatesToLoader()
        {
            await _service.RefreshCatalogIfNeededAsync();

            Assert.AreEqual(1, _loader.CatalogRefreshCalls);
        }

        [Test]
        public async Task ReleaseAll_WithCachedEntries_CallsLoaderReleasePerEntry()
        {
            var a = CreateConfig<TestConfig>();
            var b = CreateConfig<OtherConfig>();
            _loader.Register("Test/TestConfig", a);
            _loader.Register("Test/OtherConfig", b);
            await _service.LoadAsync<TestConfig>();
            await _service.LoadAsync<OtherConfig>();

            _service.ReleaseAll();

            Assert.AreEqual(2, _loader.ReleaseCalls);
        }

        [Test]
        public void TryGet_NotLoaded_ReturnsFalseAndNull()
        {
            var success = _service.TryGet<TestConfig>(out var result);

            Assert.IsFalse(success);
            Assert.IsNull(result);
        }

        [Test]
        public async Task TryGet_Loaded_ReturnsTrueAndInstance()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);
            await _service.LoadAsync<TestConfig>();

            var success = _service.TryGet<TestConfig>(out var result);

            Assert.IsTrue(success);
            Assert.AreSame(config, result);
        }

        [Test]
        public void TryGet_AfterDispose_ThrowsObjectDisposed()
        {
            _service.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _service.TryGet<TestConfig>(out _));
        }

        [Test]
        public void Dispose_AfterLoad_MakesServiceUnusable()
        {
            var config = CreateConfig<TestConfig>();
            _loader.Register("Test/TestConfig", config);

            _service.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _service.Get<TestConfig>());
        }

        [Test]
        public void LoadAsync_AfterDispose_ThrowsObjectDisposed()
        {
            _service.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await _service.LoadAsync<TestConfig>());
        }

        [Test]
        public void ReleaseAll_AfterDispose_ThrowsObjectDisposed()
        {
            _service.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _service.ReleaseAll());
        }

        [Test]
        public void RefreshCatalogIfNeededAsync_AfterDispose_ThrowsObjectDisposed()
        {
            _service.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await _service.RefreshCatalogIfNeededAsync());
        }

        private T CreateConfig<T>() where T : ConfigBase
        {
            var so = ScriptableObject.CreateInstance<T>();
            _createdConfigs.Add(so);
            return so;
        }

        private class FakeAssetLoader : IAssetLoader
        {
            public readonly Dictionary<string, UnityEngine.Object> Registry = new();
            public int LoadCalls;
            public int ReleaseCalls;
            public int CatalogRefreshCalls;
            public Exception? ThrowOnLoad;
            public UniTaskCompletionSource? LoadGate;

            public void Register(string address, UnityEngine.Object asset)
            {
                Registry[address] = asset;
            }

            public async UniTask<(T asset, object releaseToken)> LoadAsync<T>(string address, CancellationToken ct)
                where T : UnityEngine.Object
            {
                LoadCalls++;

                if (LoadGate != null)
                {
                    await LoadGate.Task;
                }

                ct.ThrowIfCancellationRequested();

                if (ThrowOnLoad != null)
                {
                    throw ThrowOnLoad;
                }

                if (!Registry.TryGetValue(address, out var asset))
                {
                    throw new InvalidOperationException($"FakeAssetLoader: address '{address}' not registered");
                }

                return ((T)asset, new object());
            }

            public void Release(object releaseToken)
            {
                ReleaseCalls++;
            }

            public UniTask RefreshCatalogIfNeededAsync(CancellationToken ct)
            {
                CatalogRefreshCalls++;
                return UniTask.CompletedTask;
            }
        }

        [RegisterConfig("Test/TestConfig")]
        private class TestConfig : ConfigBase { }

        [RegisterConfig("Test/OtherConfig")]
        private class OtherConfig : ConfigBase { }

        [RegisterConfig("Test/InvalidatingConfig")]
        private class InvalidatingConfig : ConfigBase
        {
            public bool ValidateResult = true;
            public override bool Validate() => ValidateResult;
        }

        private class ConfigWithoutAttribute : ConfigBase { }
    }
}
