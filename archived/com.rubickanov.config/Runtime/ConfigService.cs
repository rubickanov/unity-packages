using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Rubickanov.Config
{
    /// <summary>
    /// Default <see cref="IConfigService"/> implementation. Loads configs via a pluggable
    /// <see cref="IAssetLoader"/>, coalesces concurrent loads of the same type, and tracks
    /// release tokens so <see cref="ReleaseAll"/> can clean up on scene transitions.
    /// </summary>
    public class ConfigService : IConfigService
    {
        private readonly ILogger<ConfigService> _logger;
        private readonly IAssetLoader _loader;
        private readonly Dictionary<Type, CachedConfig> _cache = new();
        private readonly Dictionary<Type, RegisterConfigAttribute?> _attributeCache = new();
        private readonly Dictionary<Type, object> _pending = new();

        private bool _disposed;

        public ConfigService(ILoggerFactory loggerFactory, IAssetLoader loader)
        {
            _logger = loggerFactory.CreateLogger<ConfigService>();
            _loader = loader;
        }

        public UniTask RefreshCatalogIfNeededAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return _loader.RefreshCatalogIfNeededAsync(ct);
        }

        public UniTask<TConfig> LoadAsync<TConfig>(CancellationToken ct = default) where TConfig : ConfigBase
        {
            ThrowIfDisposed();

            var type = typeof(TConfig);

            if (_cache.TryGetValue(type, out var cached))
            {
                return new UniTask<TConfig>((TConfig)cached.Config);
            }

            // Coalesced loads run on an internal token, never the caller's. If the shared load
            // carried the first caller's token, that caller cancelling would fault every other
            // caller awaiting the same load. Instead each caller attaches its own token via
            // AttachExternalCancellation, so cancellation is per-caller and independent; the
            // shared load itself always runs to completion and caches.
            if (_pending.TryGetValue(type, out var pending))
            {
                return ((UniTask<TConfig>)pending).AttachExternalCancellation(ct);
            }

            var preserved = LoadInternalAsync<TConfig>(type, CancellationToken.None).Preserve();
            _pending[type] = preserved;
            return AwaitAndCleanup(type, preserved).AttachExternalCancellation(ct);
        }

        public TConfig Get<TConfig>() where TConfig : ConfigBase
        {
            ThrowIfDisposed();

            var type = typeof(TConfig);

            if (_cache.TryGetValue(type, out var cached))
            {
                return (TConfig)cached.Config;
            }

            throw new InvalidOperationException(
                $"Config {type.Name} is not loaded. Call LoadAsync<{type.Name}>() first.");
        }

        public bool TryGet<TConfig>(out TConfig config) where TConfig : ConfigBase
        {
            ThrowIfDisposed();

            if (_cache.TryGetValue(typeof(TConfig), out var cached))
            {
                config = (TConfig)cached.Config;
                return true;
            }

            config = null!;
            return false;
        }

        public void ReleaseAll()
        {
            ThrowIfDisposed();
            ReleaseAllInternal();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ReleaseAllInternal();
            _disposed = true;
        }

        private async UniTask<TConfig> LoadInternalAsync<TConfig>(Type type, CancellationToken ct)
            where TConfig : ConfigBase
        {
            var attribute = GetConfigAttribute(type);

            if (attribute == null)
            {
                throw new InvalidOperationException(
                    $"No [RegisterConfig] attribute on {type.Name}.");
            }

            _logger.LogDebug("Loading {Type} from {Address}", type.Name, attribute.Address);

            TConfig config;
            object releaseToken;
            try
            {
                (config, releaseToken) = await _loader.LoadAsync<TConfig>(attribute.Address, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load {Type} from {Address}", type.Name, attribute.Address);
                throw;
            }

            // Dispose() may have run while the load was in flight: ReleaseAllInternal already
            // cleared the cache, so caching this handle now would leak it for the process
            // lifetime. Release it and bail instead of repopulating a disposed service.
            if (_disposed)
            {
                _loader.Release(releaseToken);
                throw new ObjectDisposedException(nameof(ConfigService));
            }

            if (!config.Validate())
            {
                _logger.LogError("{Type} validation failed at {Address}", type.Name, attribute.Address);
                _loader.Release(releaseToken);
                throw new InvalidOperationException(
                    $"{type.Name} failed Validate() — loaded from '{attribute.Address}'.");
            }

            _cache[type] = new CachedConfig(config, releaseToken);
            return config;
        }

        private async UniTask<TConfig> AwaitAndCleanup<TConfig>(Type type, UniTask<TConfig> task)
            where TConfig : ConfigBase
        {
            try
            {
                return await task;
            }
            finally
            {
                _pending.Remove(type);
            }
        }

        private void ReleaseAllInternal()
        {
            foreach (var entry in _cache.Values)
            {
                _loader.Release(entry.ReleaseToken);
            }

            _cache.Clear();
            _logger.LogDebug("Released all cached configs");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConfigService));
            }
        }

        private RegisterConfigAttribute? GetConfigAttribute(Type type)
        {
            if (_attributeCache.TryGetValue(type, out var cached))
                return cached;

            var attribute = type.GetCustomAttribute<RegisterConfigAttribute>();
            _attributeCache[type] = attribute;
            return attribute;
        }

        private readonly struct CachedConfig
        {
            public readonly ConfigBase Config;
            public readonly object ReleaseToken;

            public CachedConfig(ConfigBase config, object releaseToken)
            {
                Config = config;
                ReleaseToken = releaseToken;
            }
        }
    }
}
