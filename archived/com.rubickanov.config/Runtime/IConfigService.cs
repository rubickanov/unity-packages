using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Config
{
    public interface IConfigService : IDisposable
    {
        /// <summary>
        /// Load a config by type. Address is resolved from [RegisterConfig] attribute.
        /// Returns cached instance if already loaded. Concurrent calls for the same
        /// type are coalesced — the underlying asset is loaded once.
        /// </summary>
        UniTask<TConfig> LoadAsync<TConfig>(CancellationToken ct = default) where TConfig : ConfigBase;

        /// <summary>
        /// Get an already-loaded config by type.
        /// Throws InvalidOperationException if not loaded yet.
        /// </summary>
        TConfig Get<TConfig>() where TConfig : ConfigBase;

        /// <summary>
        /// Try to get an already-loaded config without throwing.
        /// Returns true and sets <paramref name="config"/> when the config is cached;
        /// returns false otherwise.
        /// </summary>
        bool TryGet<TConfig>(out TConfig config) where TConfig : ConfigBase;

        /// <summary>
        /// Check for catalog updates and download if available.
        /// Call before loading configs to ensure fresh data from server.
        /// </summary>
        UniTask RefreshCatalogIfNeededAsync(CancellationToken ct = default);

        /// <summary>
        /// Release all cached configs and their loader handles.
        /// Call between scenes before reloading configs.
        /// </summary>
        void ReleaseAll();
    }
}
