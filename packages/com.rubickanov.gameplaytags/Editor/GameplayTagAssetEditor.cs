using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="GameplayTagAsset"/>. Shows tag hierarchy with add/remove and validation.
    /// </summary>
    [CustomEditor(typeof(GameplayTagAsset))]
    public sealed class GameplayTagAssetEditor : UnityEditor.Editor
    {
        private static readonly Regex TagPathRegex = new(@"^[a-zA-Z][a-zA-Z0-9]*(\.[a-zA-Z][a-zA-Z0-9]*)*$");

        private string _newTagPath = "";

        private GameplayTagRegistry? _cachedRegistry;
        private IReadOnlyList<string>? _cachedNames;
        private int _cachedPathCount;
        private int _cachedPathHash;

        public override void OnInspectorGUI()
        {
            var asset = (GameplayTagAsset)target;
            var paths = asset.TagPaths.ToList();

            var (registry, allNames) = GetOrRebuildRegistry(paths);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"Tags ({allNames.Count})", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Add tag field
            EditorGUILayout.BeginHorizontal();
            _newTagPath = EditorGUILayout.TextField(_newTagPath);

            var isValid = !string.IsNullOrWhiteSpace(_newTagPath) && TagPathRegex.IsMatch(_newTagPath);
            var isDuplicate = paths.Contains(_newTagPath);

            EditorGUI.BeginDisabledGroup(!isValid || isDuplicate);
            if (GUILayout.Button("Add Tag", GUILayout.Width(80)))
            {
                Undo.RecordObject(asset, "Add Gameplay Tag");
                var newPaths = new List<string>(paths) { _newTagPath };
                asset.SetTagPaths(newPaths.ToArray());
                EditorUtility.SetDirty(asset);
                InvalidateCache();
                _newTagPath = "";
                GUI.FocusControl(null);
                return;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_newTagPath) && !isValid)
            {
                EditorGUILayout.HelpBox(
                    "Invalid tag path. Use format: Segment.Segment (alphanumeric, starts with letter).",
                    MessageType.Warning);
            }

            if (isDuplicate)
            {
                EditorGUILayout.HelpBox("This tag already exists.", MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            // Tag list with hierarchy
            string? tagToRemove = null;

            foreach (var name in allNames)
            {
                var depth = name.Split('.').Length - 1;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 16f);

                EditorGUILayout.LabelField(name, GUILayout.ExpandWidth(true));

                if (GUILayout.Button("\u2212", GUILayout.Width(25))) // minus sign
                {
                    tagToRemove = name;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (tagToRemove != null)
            {
                Undo.RecordObject(asset, "Remove Gameplay Tag");

                // Remove tag and all descendants
                var prefix = tagToRemove + ".";
                var newPaths = paths
                    .Where(p => p != tagToRemove && !p.StartsWith(prefix))
                    .ToArray();

                asset.SetTagPaths(newPaths);
                EditorUtility.SetDirty(asset);
                InvalidateCache();
            }
        }

        private (GameplayTagRegistry registry, IReadOnlyList<string> names) GetOrRebuildRegistry(
            List<string> paths)
        {
            var hash = ComputePathHash(paths);

            if (_cachedRegistry != null && _cachedNames != null
                && _cachedPathCount == paths.Count && _cachedPathHash == hash)
            {
                return (_cachedRegistry, _cachedNames);
            }

            _cachedRegistry = new GameplayTagRegistry(paths);
            _cachedNames = _cachedRegistry.GetAllNames();
            _cachedPathCount = paths.Count;
            _cachedPathHash = hash;

            return (_cachedRegistry, _cachedNames);
        }

        private void InvalidateCache()
        {
            _cachedRegistry = null;
            _cachedNames = null;
        }

        private static int ComputePathHash(List<string> paths)
        {
            var hash = paths.Count;
            for (var i = 0; i < paths.Count; i++)
                hash = hash * 31 + (paths[i]?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
