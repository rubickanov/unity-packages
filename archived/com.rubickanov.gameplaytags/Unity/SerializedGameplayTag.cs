using System;
using UnityEngine;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Serializable wrapper for a single <see cref="GameplayTag"/>. Stores path as string for stability.
    /// Lazy-resolves to <see cref="GameplayTag"/> via the installed registry.
    /// </summary>
    [Serializable]
    public struct SerializedGameplayTag : ISerializationCallbackReceiver
    {
        [SerializeField] private string _path;

        private GameplayTag _cachedTag;
        private bool _dirty;

        /// <summary>The serialized dot-separated tag path.</summary>
        public string Path => _path ?? "";

        /// <summary>The resolved tag. Returns <see cref="GameplayTag.None"/> if path is missing from registry.</summary>
        public GameplayTag Tag
        {
            get
            {
                if (_dirty)
                    Resolve();

                return _cachedTag;
            }
        }

        public SerializedGameplayTag(string path)
        {
            _path = path ?? "";
            _cachedTag = GameplayTag.None;
            _dirty = true;
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            _dirty = true;
        }

        private void Resolve()
        {
            _dirty = false;

            if (string.IsNullOrEmpty(_path) || !GameplayTagRegistry.IsInstalled)
            {
                _cachedTag = GameplayTag.None;
                return;
            }

            _cachedTag = GameplayTagRegistry.Instance.TryGet(_path, out var tag)
                ? tag
                : GameplayTag.None;
        }
    }
}
