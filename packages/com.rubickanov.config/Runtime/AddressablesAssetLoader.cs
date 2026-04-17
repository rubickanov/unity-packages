using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Rubickanov.Config
{
    /// <summary>
    /// Default <see cref="IAssetLoader"/> implementation backed by Unity Addressables.
    /// </summary>
    public class AddressablesAssetLoader : IAssetLoader
    {
        public async UniTask<(T asset, object releaseToken)> LoadAsync<T>(string address, CancellationToken ct)
            where T : Object
        {
            var handle = Addressables.LoadAssetAsync<T>(address);
            await handle.WithCancellation(ct);
            return (handle.Result, handle);
        }

        public void Release(object releaseToken)
        {
            if (releaseToken is AsyncOperationHandle handle && handle.IsValid())
            {
                Addressables.Release(handle);
            }
        }

        public async UniTask RefreshCatalogIfNeededAsync(CancellationToken ct)
        {
            var updates = await Addressables.CheckForCatalogUpdates().WithCancellation(ct);
            if (updates.Count > 0)
            {
                await Addressables.UpdateCatalogs(updates).WithCancellation(ct);
            }
        }
    }
}
