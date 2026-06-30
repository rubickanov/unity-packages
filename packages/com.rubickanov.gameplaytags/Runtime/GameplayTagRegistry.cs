using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Singleton registry that owns the tag hierarchy, name-to-index mapping, and parent-chain matching.
    /// Extensible at runtime via <see cref="AddTags"/> — new tags get stable indices appended to the end.
    /// </summary>
    public sealed class GameplayTagRegistry
    {
        /// <summary>
        /// Canonical format for tag paths: dot-separated alphanumeric segments, each starting with a letter.
        /// </summary>
        public static readonly Regex TagPathRegex =
            new(@"^[a-zA-Z][a-zA-Z0-9]*(\.[a-zA-Z][a-zA-Z0-9]*)*$", RegexOptions.Compiled);

        private static GameplayTagRegistry? _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Current installed registry. Throws if not installed. Main thread only.</summary>
        public static GameplayTagRegistry Instance =>
            _instance ?? throw new InvalidOperationException(
                "GameplayTagRegistry is not installed. Call GameplayTagRegistry.Install() first.");

        /// <summary>Whether a registry has been installed.</summary>
        public static bool IsInstalled => _instance != null;

        /// <summary>Installs the registry as the global singleton. Throws if already installed. Main thread only.</summary>
        public static void Install(GameplayTagRegistry registry)
        {
            if (_instance != null)
                throw new InvalidOperationException(
                    "GameplayTagRegistry is already installed. Call Uninstall() first.");

            _instance = registry;
        }

        /// <summary>
        /// Removes the installed registry. Safe to call if not installed.
        /// Test-only: previously cached <see cref="GameplayTag"/> values become stale on a subsequent Install with a different tag set.
        /// </summary>
        public static void Uninstall()
        {
            _instance = null;
        }

        // Index 0 is reserved for None. All valid tags live at index >= 1.
        private readonly List<string> _names;
        private readonly List<int> _parents;
        private readonly List<int> _depths;
        private readonly Dictionary<string, int> _nameToIndex;

        private ReadOnlyCollection<GameplayTag>? _sortedTagsCache;
        private ReadOnlyCollection<string>? _sortedNamesCache;

        /// <summary>Number of registered tags (excluding None).</summary>
        public int Count => _names.Count - 1;

        /// <summary>Creates an empty registry. Tags can be added via <see cref="AddTags"/>.</summary>
        public GameplayTagRegistry()
        {
            _names = new List<string> { "" };
            _parents = new List<int> { 0 };
            _depths = new List<int> { 0 };
            _nameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Creates a registry from tag paths. Auto-creates parent tags and assigns indices.
        /// Null/empty/whitespace entries are skipped. Invalid non-empty paths throw <see cref="ArgumentException"/>.
        /// </summary>
        public GameplayTagRegistry(IReadOnlyList<string> tagPaths) : this()
        {
            AddTags(tagPaths);
        }

        /// <summary>
        /// Appends new tag paths to the registry. Existing paths are silently skipped.
        /// Missing parent prefixes are auto-created. Preserves existing indices.
        /// Null/empty/whitespace entries are skipped. Invalid non-empty paths throw <see cref="ArgumentException"/>.
        /// </summary>
        public void AddTags(IReadOnlyList<string> tagPaths)
        {
            if (tagPaths == null)
                throw new ArgumentNullException(nameof(tagPaths));

            var newPaths = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < tagPaths.Count; i++)
            {
                var path = tagPaths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var trimmed = path.Trim();

                if (!TagPathRegex.IsMatch(trimmed))
                    throw new ArgumentException(
                        $"Invalid gameplay tag path '{trimmed}'. Expected format: dot-separated alphanumeric segments, each starting with a letter (e.g. 'Damage.Fire.DoT').",
                        nameof(tagPaths));

                if (_nameToIndex.ContainsKey(trimmed))
                    continue;

                newPaths.Add(trimmed);

                // Collect missing parent prefixes
                var dotIndex = -1;
                while ((dotIndex = trimmed.IndexOf('.', dotIndex + 1)) >= 0)
                {
                    var prefix = trimmed.Substring(0, dotIndex);
                    if (!_nameToIndex.ContainsKey(prefix))
                        newPaths.Add(prefix);
                }
            }

            if (newPaths.Count == 0)
                return;

            // Sort so parents come before children when we resolve parent indices
            var sorted = new List<string>(newPaths);
            sorted.Sort(StringComparer.Ordinal);

            for (var i = 0; i < sorted.Count; i++)
            {
                var name = sorted[i];
                var index = _names.Count;

                _names.Add(name);
                _nameToIndex[name] = index;
                _depths.Add(CountSegments(name));

                var lastDot = name.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    var parentName = name.Substring(0, lastDot);
                    _parents.Add(_nameToIndex.TryGetValue(parentName, out var parentIndex)
                        ? parentIndex
                        : 0);
                }
                else
                {
                    _parents.Add(0);
                }
            }

            _sortedTagsCache = null;
            _sortedNamesCache = null;
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
            if (tag.Index < 0 || tag.Index >= _names.Count)
                return "";

            return _names[tag.Index];
        }

        /// <summary>Returns the parent tag, or <see cref="GameplayTag.None"/> for root-level tags.</summary>
        public GameplayTag GetParent(GameplayTag tag)
        {
            if (tag.Index <= 0 || tag.Index >= _parents.Count)
                return GameplayTag.None;

            return new GameplayTag(_parents[tag.Index]);
        }

        /// <summary>Returns the depth of a tag (number of segments). Root tags have depth 1.</summary>
        public int GetDepth(GameplayTag tag)
        {
            if (tag.Index <= 0 || tag.Index >= _depths.Count)
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

            // Upper-bound guard: a stale/out-of-range index (e.g. a tag cached against a larger
            // registry, or across Uninstall + Install with a smaller set) must degrade to "no
            // match" rather than throw IndexOutOfRangeException when walking the parent chain.
            if (tag.Index >= _parents.Count || parent.Index >= _parents.Count)
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

        /// <summary>Returns all registered tags in lexicographic order.</summary>
        public IReadOnlyList<GameplayTag> GetAllTags()
        {
            if (_sortedTagsCache != null)
                return _sortedTagsCache;

            EnsureSortedCache();
            return _sortedTagsCache!;
        }

        /// <summary>Returns all registered tag paths in lexicographic order.</summary>
        public IReadOnlyList<string> GetAllNames()
        {
            if (_sortedNamesCache != null)
                return _sortedNamesCache;

            EnsureSortedCache();
            return _sortedNamesCache!;
        }

        private void EnsureSortedCache()
        {
            var count = _names.Count - 1;
            var names = new List<string>(count);
            for (var i = 1; i < _names.Count; i++)
                names.Add(_names[i]);
            names.Sort(StringComparer.Ordinal);

            var tags = new List<GameplayTag>(count);
            for (var i = 0; i < names.Count; i++)
                tags.Add(new GameplayTag(_nameToIndex[names[i]]));

            _sortedNamesCache = new ReadOnlyCollection<string>(names);
            _sortedTagsCache = new ReadOnlyCollection<GameplayTag>(tags);
        }

        private static int CountSegments(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            var count = 1;
            for (var i = 0; i < name.Length; i++)
                if (name[i] == '.')
                    count++;
            return count;
        }
    }
}
