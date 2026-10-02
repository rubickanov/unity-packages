using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Serializable wrapper for a <see cref="GameplayTagContainer"/>. Stores paths as strings.
    /// Lazy-resolves to a <see cref="ReadOnlyGameplayTagContainer"/> view via the installed registry.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="ReadOnlyGameplayTagContainer"/> is immutable from the outside:
    /// callers cannot mutate container state through the accessor. To change the tag set, modify
    /// <see cref="Paths"/> (e.g. via the owning serialized field) and call <see cref="OnAfterDeserialize"/>.
    /// </remarks>
    [Serializable]
    public struct SerializedGameplayTagContainer : ISerializationCallbackReceiver
    {
        [SerializeField] private string[] _paths;

        private GameplayTagContainer? _cachedContainer;
        private bool _dirty;

        /// <summary>The serialized tag paths.</summary>
        public IReadOnlyList<string> Paths => _paths ?? Array.Empty<string>();

        /// <summary>Read-only view of the resolved container. Returns an empty view if registry is not installed.</summary>
        public ReadOnlyGameplayTagContainer Container
        {
            get
            {
                if (_dirty || _cachedContainer == null)
                    Resolve();

                return new ReadOnlyGameplayTagContainer(_cachedContainer!);
            }
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            _dirty = true;
        }

        private void Resolve()
        {
            _dirty = false;
            _cachedContainer = new GameplayTagContainer();

            var paths = _paths;
            if (paths == null || !GameplayTagRegistry.IsInstalled)
                return;

            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                if (GameplayTagRegistry.Instance.TryGet(path, out var tag))
                    _cachedContainer.AddTag(tag);
            }
        }
    }
}
