using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a nested class per <c>.uxml</c> document holding a string constant for each named
    /// element (its <c>name="..."</c> attribute), so views query with
    /// <c>Root.Q&lt;Button&gt;(UI.HudView.ReloadBtn)</c> instead of a raw string literal. The
    /// per-document grouping matches the view-to-UXML one-to-one mapping in the UI package.
    /// </summary>
    public sealed class UIToolkitNamesGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "uiToolkitNames";

        public override string Id => GeneratorId;
        public override string DisplayName => "UI Toolkit Names";
        protected override string DefaultClassName => "UI";

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:VisualTreeAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                    continue;

                IReadOnlyList<string> names;
                try
                {
                    names = ExtractElementNames(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Codegen] Skipped UXML '{path}': {e.Message}");
                    continue;
                }

                if (names.Count == 0)
                    continue;

                var group = new ConstGroup(Path.GetFileNameWithoutExtension(path));
                foreach (var name in names)
                    group.Members.Add(new ConstMember(name, "string", Str(name)));

                groups.Add(group);
            }

            // Deterministic group order across runs.
            groups.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }

        public override bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
            => HasUxml(importedAssets) || HasUxml(deletedAssets) || HasUxml(movedAssets);

        /// <summary>
        /// Returns the distinct <c>name</c> attribute values of all elements in a UXML document, in
        /// document order. Pure — unit-testable without asset state. Throws on malformed XML.
        /// </summary>
        public static IReadOnlyList<string> ExtractElementNames(string uxml)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(uxml))
                return result;

            var document = XDocument.Parse(uxml);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var element in document.Descendants())
            {
                // The UXML "name" attribute is unqualified (no namespace), regardless of the
                // element's UIElements namespace prefix.
                var value = element.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(value) && seen.Add(value))
                    result.Add(value);
            }

            return result;
        }

        private static bool HasUxml(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
