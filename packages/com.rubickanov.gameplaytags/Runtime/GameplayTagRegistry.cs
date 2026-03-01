using System;
using System.Collections.Generic;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Singleton registry that owns the tag hierarchy, name-to-index mapping, and parent-chain matching.
    /// </summary>
    public sealed class GameplayTagRegistry
    {
        private static GameplayTagRegistry? _instance;

        /// <summary>Current installed registry. Throws if not installed.</summary>
        public static GameplayTagRegistry Instance =>
            _instance ?? throw new InvalidOperationException(
                "GameplayTagRegistry is not installed. Call GameplayTagRegistry.Install() first.");

        /// <summary>Whether a registry has been installed.</summary>
        public static bool IsInstalled => _instance != null;

        /// <summary>Installs the registry as the global singleton. Throws if already installed.</summary>
        public static void Install(GameplayTagRegistry registry)
        {
            if (_instance != null)
                throw new InvalidOperationException(
                    "GameplayTagRegistry is already installed. Call Uninstall() first.");

            _instance = registry;
        }

        /// <summary>Removes the installed registry. Safe to call if not installed.</summary>
        public static void Uninstall()
        {
            _instance = null;
        }

        private readonly string[] _names;
        private readonly int[] _parents;
        private readonly int[] _depths;
        private readonly Dictionary<string, int> _nameToIndex;
        private readonly List<GameplayTag> _allTags;
        private readonly List<string> _allNames;

        /// <summary>Number of registered tags (excluding None).</summary>
        public int Count => _names.Length - 1;

        /// <summary>
        /// Creates a registry from tag paths. Auto-creates parent tags, sorts lexicographically, assigns indices.
        /// </summary>
        public GameplayTagRegistry(IReadOnlyList<string> tagPaths)
        {
            var uniquePaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in tagPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var trimmed = path.Trim();
                uniquePaths.Add(trimmed);

                // Add all parent prefixes
                var parts = trimmed.Split('.');
                for (var i = 1; i < parts.Length; i++)
                {
                    var prefix = string.Join(".", parts, 0, i);
                    uniquePaths.Add(prefix);
                }
            }

            var sorted = new List<string>(uniquePaths);
            sorted.Sort(StringComparer.Ordinal);

            var count = sorted.Count + 1; // +1 for index 0 (None)
            _names = new string[count];
            _parents = new int[count];
            _depths = new int[count];
            _nameToIndex = new Dictionary<string, int>(count, StringComparer.Ordinal);

            // Index 0 = None
            _names[0] = "";
            _parents[0] = 0;
            _depths[0] = 0;

            for (var i = 0; i < sorted.Count; i++)
            {
                var index = i + 1;
                var name = sorted[i];
                _names[index] = name;
                _nameToIndex[name] = index;

                var dotIndex = name.LastIndexOf('.');
                _depths[index] = name.Split('.').Length;

                if (dotIndex >= 0)
                {
                    var parentName = name.Substring(0, dotIndex);
                    _parents[index] = _nameToIndex.TryGetValue(parentName, out var parentIndex)
                        ? parentIndex
                        : 0;
                }
                else
                {
                    _parents[index] = 0;
                }
            }

            _allTags = new List<GameplayTag>(sorted.Count);
            _allNames = new List<string>(sorted.Count);

            for (var i = 1; i < count; i++)
            {
                _allTags.Add(new GameplayTag(i));
                _allNames.Add(_names[i]);
            }
        }

        /// <summary>Gets a tag by its dot-separated path. Throws if not found.</summary>
        public GameplayTag Get(string path)
        {
            if (_nameToIndex.TryGetValue(path, out var index))
                return new GameplayTag(index);

            throw new ArgumentException($"GameplayTag '{path}' not found in registry.");
        }

        /// <summary>Tries to get a tag by path. Returns false and <see cref="GameplayTag.None"/> if not found.</summary>
        public bool TryGet(string path, out GameplayTag tag)
        {
            if (_nameToIndex.TryGetValue(path, out var index))
            {
                tag = new GameplayTag(index);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>Returns the full dot-separated path of a tag.</summary>
        public string GetName(GameplayTag tag)
        {
            if (tag.Index < 0 || tag.Index >= _names.Length)
                return "";

            return _names[tag.Index];
        }

        /// <summary>Returns the parent tag, or <see cref="GameplayTag.None"/> for root-level tags.</summary>
        public GameplayTag GetParent(GameplayTag tag)
        {
            if (tag.Index <= 0 || tag.Index >= _parents.Length)
                return GameplayTag.None;

            return new GameplayTag(_parents[tag.Index]);
        }

        /// <summary>Returns the depth of a tag (number of segments). Root tags have depth 1.</summary>
        public int GetDepth(GameplayTag tag)
        {
            if (tag.Index <= 0 || tag.Index >= _depths.Length)
                return 0;

            return _depths[tag.Index];
        }

        /// <summary>
        /// Hierarchical match: returns true if <paramref name="tag"/> equals or descends from <paramref name="parent"/>.
        /// Walks the parent chain, O(depth).
        /// </summary>
        public bool Matches(GameplayTag tag, GameplayTag parent)
        {
            if (tag.Index <= 0 || parent.Index <= 0)
                return false;

            var current = tag.Index;
            while (current > 0)
            {
                if (current == parent.Index)
                    return true;

                current = _parents[current];
            }

            return false;
        }

        /// <summary>Returns all registered tags in sorted order.</summary>
        public IReadOnlyList<GameplayTag> GetAllTags() => _allTags;

        /// <summary>Returns all registered tag paths in sorted order.</summary>
        public IReadOnlyList<string> GetAllNames() => _allNames;
    }
}
