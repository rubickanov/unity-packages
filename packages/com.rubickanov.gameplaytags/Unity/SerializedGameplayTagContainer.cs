using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Serializable wrapper for a <see cref="GameplayTagContainer"/>. Stores paths as strings.
    /// Lazy-resolves to <see cref="GameplayTagContainer"/> via the installed registry.
    /// </summary>
    [Serializable]
    public struct SerializedGameplayTagContainer : ISerializationCallbackReceiver
    {
        [SerializeField] private string[] _paths;

        private GameplayTagContainer? _cachedContainer;
        private bool _dirty;

        /// <summary>The serialized tag paths.</summary>
        public IReadOnlyList<string> Paths => _paths ?? Array.Empty<string>();

        /// <summary>The resolved container. Returns empty container if registry is not installed.</summary>
        public GameplayTagContainer Container
        {
            get
            {
                if (_dirty || _cachedContainer == null)
                    Resolve();

                return _cachedContainer!;
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
