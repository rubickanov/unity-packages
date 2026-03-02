using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Config
{
    public interface IConfigService : IDisposable
    {
        /// <summary>
        /// Load a config by type. Address is resolved from [RegisterConfig] attribute.
        /// Returns cached instance if already loaded.
        /// </summary>
        UniTask<TConfig> LoadAsync<TConfig>() where TConfig : ConfigBase;

        /// <summary>
        /// Get an already-loaded config by type.
        /// Throws InvalidOperationException if not loaded yet.
        /// </summary>
        TConfig Get<TConfig>() where TConfig : ConfigBase;

        /// <summary>
        /// Check for catalog updates and download if available.
        /// Call before loading configs to ensure fresh data from server.
        /// </summary>
        UniTask RefreshCatalogIfNeededAsync();

        /// <summary>
        /// Release all cached configs and their Addressable handles.
        /// Call between scenes before reloading configs.
        /// </summary>
        void ReleaseAll();
    }
}
