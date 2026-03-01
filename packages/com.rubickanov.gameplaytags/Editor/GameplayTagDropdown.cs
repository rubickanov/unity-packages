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

            var asset = FindTagAsset();
            if (asset == null)
                return root;

            var registry = new GameplayTagRegistry(asset.TagPaths);
            var names = registry.GetAllNames();

            var nodeMap = new Dictionary<string, GameplayTagDropdownItem>();

            foreach (var name in names)
            {
                var parts = name.Split('.');
                AdvancedDropdownItem parentItem = root;

                for (var i = 0; i < parts.Length; i++)
                {
                    var key = string.Join(".", parts, 0, i + 1);

                    if (!nodeMap.TryGetValue(key, out var item))
                    {
                        item = new GameplayTagDropdownItem(parts[i], key);
                        nodeMap[key] = item;
                        parentItem.AddChild(item);
                    }

                    parentItem = item;
                }
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

        private static GameplayTagAsset? FindTagAsset()
        {
            var guids = AssetDatabase.FindAssets("t:GameplayTagAsset");
            if (guids.Length == 0)
                return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<GameplayTagAsset>(path);
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
