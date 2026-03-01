using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// ScriptableObject database that stores all gameplay tag paths.
    /// </summary>
    [CreateAssetMenu(menuName = "Config/Gameplay Tags")]
    public class GameplayTagAsset : ScriptableObject
    {
        [SerializeField] private string[] _tagPaths = System.Array.Empty<string>();

        /// <summary>All registered tag paths.</summary>
        public IReadOnlyList<string> TagPaths => _tagPaths;

        /// <summary>Creates a new <see cref="GameplayTagRegistry"/> from this asset's tag paths.</summary>
        public GameplayTagRegistry BuildRegistry() => new(_tagPaths);

#if UNITY_EDITOR
        internal void SetTagPaths(string[] paths)
        {
            _tagPaths = paths;
        }
#endif
    }
}
