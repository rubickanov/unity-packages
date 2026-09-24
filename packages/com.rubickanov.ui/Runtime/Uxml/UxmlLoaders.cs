using System;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public static class UxmlLoaders
    {
        /// <summary>
        /// A loader that returns assets from <paramref name="catalog"/>. It completes synchronously and its handles
        /// release nothing: the catalog holds the references.
        /// </summary>
        public static UxmlLoader FromCatalog(UxmlCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            return name =>
            {
                if (!catalog.TryGet(name, out var asset))
                    throw new InvalidOperationException(
                        $"UXML catalog '{catalog.name}' has no asset '{name}' for the view with that UxmlName. " +
                        "Add the view's UXML to the catalog.");
                return UniTask.FromResult<(VisualTreeAsset? asset, IDisposable? handle)>((asset, NoRelease.Instance));
            };
        }

        private sealed class NoRelease : IDisposable
        {
            public static readonly NoRelease Instance = new();
            public void Dispose() { }
        }
    }
}
