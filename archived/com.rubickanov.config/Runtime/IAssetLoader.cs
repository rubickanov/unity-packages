using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.Config
{
    /// <summary>
    /// Abstraction over asset loading so <see cref="ConfigService"/> can be tested
    /// without pulling in the Addressables runtime.
    /// </summary>
    public interface IAssetLoader
    {
        /// <summary>
        /// Load an asset of type <typeparamref name="T"/> from the given address.
        /// Returns the asset and an opaque release token to pass back to <see cref="Release"/>.
        /// </summary>
        UniTask<(T asset, object releaseToken)> LoadAsync<T>(string address, CancellationToken ct)
            where T : Object;

        /// <summary>
        /// Release a previously loaded asset by its release token.
        /// </summary>
        void Release(object releaseToken);

        /// <summary>
        /// Check for catalog updates and apply them if available.
        /// </summary>
        UniTask RefreshCatalogIfNeededAsync(CancellationToken ct);
    }
}
