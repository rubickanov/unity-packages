using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Searchable hierarchical tree picker for gameplay tags.
    /// </summary>
    public sealed class GameplayTagDropdown : AdvancedDropdown
    {
        private readonly Action<string> _onSelected;

        public GameplayTagDropdown(AdvancedDropdownState state, Action<string> onSelected)
            : base(state)
        {
            _onSelected = onSelected;
            minimumSize = new Vector2(250, 300);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Gameplay Tags");
            root.AddChild(new AdvancedDropdownItem("None"));

            var allNames = CollectAllTagNames();
            if (allNames.Count == 0)
            {
                root.AddChild(new AdvancedDropdownItem("(no GameplayTagAsset found — create one via Assets > Create > Config > Gameplay Tags)"));
                return root;
            }

            var nodeMap = new Dictionary<string, GameplayTagDropdownItem>();
            var parentKeys = new HashSet<string>();

            foreach (var name in allNames)
            {
                AdvancedDropdownItem parentItem = root;
                var start = 0;
                var segmentEnd = -1;

                while (start <= name.Length)
                {
                    segmentEnd = name.IndexOf('.', start);
                    var keyEnd = segmentEnd < 0 ? name.Length : segmentEnd;
                    var key = name.Substring(0, keyEnd);
                    var displayName = name.Substring(start, keyEnd - start);

                    if (!nodeMap.TryGetValue(key, out var item))
                    {
                        item = new GameplayTagDropdownItem(displayName, key);
                        nodeMap[key] = item;
                        parentItem.AddChild(item);
                    }

                    if (parentItem is GameplayTagDropdownItem parentTagItem)
                        parentKeys.Add(parentTagItem.FullPath);

                    parentItem = item;

                    if (segmentEnd < 0)
                        break;

                    start = segmentEnd + 1;
                }
            }

            foreach (var parentKey in parentKeys)
            {
                var node = nodeMap[parentKey];
                node.AddChild(new GameplayTagDropdownItem($"(select {node.name})", parentKey));
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item.name == "None")
            {
                _onSelected("");
                return;
            }

            if (item is GameplayTagDropdownItem tagItem)
                _onSelected(tagItem.FullPath);
        }

        private static IReadOnlyList<string> CollectAllTagNames()
        {
            var guids = AssetDatabase.FindAssets("t:GameplayTagAsset");
            if (guids.Length == 0)
                return Array.Empty<string>();

            var merged = new List<string>();
            var seen = new HashSet<string>();

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<GameplayTagAsset>(path);
                if (asset == null)
                    continue;

                foreach (var tagPath in asset.TagPaths)
                {
                    if (string.IsNullOrWhiteSpace(tagPath))
                        continue;

                    if (seen.Add(tagPath))
                        merged.Add(tagPath);
                }
            }

            if (merged.Count == 0)
                return Array.Empty<string>();

            var registry = new GameplayTagRegistry(merged);
            return registry.GetAllNames();
        }

        private sealed class GameplayTagDropdownItem : AdvancedDropdownItem
        {
            public string FullPath { get; }

            public GameplayTagDropdownItem(string displayName, string fullPath)
                : base(displayName)
            {
                FullPath = fullPath;
            }
        }
    }
}
