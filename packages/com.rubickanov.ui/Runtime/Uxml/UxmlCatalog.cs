using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// UXML assets referenced directly, looked up by asset name. A view's asset is named after its <c>UxmlName</c>,
    /// which defaults to the view type name. Load through <see cref="UxmlLoaders.FromCatalog"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "UxmlCatalog", menuName = "Rubickanov/UI/UXML Catalog")]
    public sealed class UxmlCatalog : ScriptableObject
    {
        [SerializeField] private List<VisualTreeAsset> _assets = new();

        private Dictionary<string, VisualTreeAsset>? _byName;

        public IReadOnlyList<VisualTreeAsset> Assets => _assets;

        /// <summary>Replaces the assets, for a catalog built in code.</summary>
        public void SetAssets(IEnumerable<VisualTreeAsset> assets)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            _assets = new List<VisualTreeAsset>(assets);
            _byName = null;
        }

        /// <exception cref="InvalidOperationException">Two assets share a name.</exception>
        public bool TryGet(string name, out VisualTreeAsset asset)
        {
            _byName ??= BuildLookup();
            return _byName.TryGetValue(name, out asset!);
        }

        /// <summary>
        /// Returns the view types whose <c>UxmlName</c> has no asset in this catalog. A view with <c>UxmlName</c>
        /// <c>null</c> needs none. Call it from an EditMode test over the project's view types.
        /// </summary>
        /// <exception cref="ArgumentException">A type is not a concrete <see cref="View"/>.</exception>
        public IReadOnlyList<Type> FindMissing(IEnumerable<Type> viewTypes)
        {
            if (viewTypes == null) throw new ArgumentNullException(nameof(viewTypes));

            var missing = new List<Type>();
            foreach (var type in viewTypes)
            {
                if (type == null || type.IsAbstract || !typeof(View).IsAssignableFrom(type))
                    throw new ArgumentException($"'{type?.FullName}' is not a concrete view type.", nameof(viewTypes));

                var view = (View)Activator.CreateInstance(type);
                var uxmlName = view.ResolveUxmlName();
                if (uxmlName != null && !TryGet(uxmlName, out _))
                    missing.Add(type);
            }
            return missing;
        }

        private Dictionary<string, VisualTreeAsset> BuildLookup()
        {
            var byName = new Dictionary<string, VisualTreeAsset>(_assets.Count, StringComparer.Ordinal);
            var indices = new Dictionary<string, int>(_assets.Count, StringComparer.Ordinal);
            for (int i = 0; i < _assets.Count; i++)
            {
                var asset = _assets[i];
                if (asset == null) continue;

                if (byName.ContainsKey(asset.name))
                    throw new InvalidOperationException(
                        $"UXML catalog '{name}' has two assets named '{asset.name}', at indices " +
                        $"{indices[asset.name]} and {i}.");

                byName.Add(asset.name, asset);
                indices.Add(asset.name, i);
            }
            return byName;
        }

        private void OnValidate() => _byName = null;
    }
}
