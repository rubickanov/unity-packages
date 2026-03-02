using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Rubickanov.Config
{
    public class ConfigService : IConfigService
    {
        private readonly ILogger<ConfigService> _logger;
        private readonly Dictionary<Type, CachedConfig> _cache = new();

        private static readonly Dictionary<Type, RegisterConfigAttribute?> _attributeCache = new();

        public ConfigService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ConfigService>();
        }

        public async UniTask RefreshCatalogIfNeededAsync()
        {
            var updates = await Addressables.CheckForCatalogUpdates();

            if (updates.Count == 0)
            {
                _logger.LogDebug("Catalog is up to date");
                return;
            }

            _logger.LogInformation("Updating {Count} catalogs", updates.Count);
            await Addressables.UpdateCatalogs(updates);
            _logger.LogInformation("Catalogs updated");
        }

        public async UniTask<TConfig> LoadAsync<TConfig>() where TConfig : ConfigBase
        {
            var type = typeof(TConfig);

            if (_cache.TryGetValue(type, out var cached))
            {
                return (TConfig)cached.Config;
            }

            var attribute = GetConfigAttribute(type);

            if (attribute == null)
            {
                throw new InvalidOperationException(
                    $"No [RegisterConfig] attribute on {type.Name}.");
            }

            _logger.LogDebug("Loading {Type} from {Address}", type.Name, attribute.Address);

            AsyncOperationHandle<TConfig> handle;
            try
            {
                handle = Addressables.LoadAssetAsync<TConfig>(attribute.Address);
                await handle;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load {Type} from {Address}", type.Name, attribute.Address);
                throw;
            }

            var config = handle.Result;

            if (!config.Validate())
            {
                _logger.LogWarning("{Type} validation failed", type.Name);
            }

            _cache[type] = new CachedConfig(config, handle);
            return config;
        }

        public TConfig Get<TConfig>() where TConfig : ConfigBase
        {
            var type = typeof(TConfig);

            if (_cache.TryGetValue(type, out var cached))
            {
                return (TConfig)cached.Config;
            }

            throw new InvalidOperationException(
                $"Config {type.Name} is not loaded. Call LoadAsync<{type.Name}>() first.");
        }

        public void ReleaseAll()
        {
            foreach (var entry in _cache.Values)
            {
                if (entry.Handle.IsValid())
                {
                    Addressables.Release(entry.Handle);
                }
            }

            _cache.Clear();
            _logger.LogDebug("Released all cached configs");
        }

        public void Dispose()
        {
            ReleaseAll();
        }

        private static RegisterConfigAttribute? GetConfigAttribute(Type type)
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
            public readonly AsyncOperationHandle Handle;

            public CachedConfig(ConfigBase config, AsyncOperationHandle handle)
            {
                Config = config;
                Handle = handle;
            }
        }
    }
}
