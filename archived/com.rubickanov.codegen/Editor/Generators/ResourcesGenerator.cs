using System;
using System.Collections.Generic;
using UnityEditor;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a string constant for every asset under an <c>Assets/.../Resources/</c> folder, keyed
    /// by the path <see cref="UnityEngine.Resources.Load(string)"/> expects (relative to the
    /// Resources root, without extension), so resource loads use a checked constant instead of a
    /// magic string.
    /// </summary>
    public sealed class ResourcesGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "resources";

        private const string Marker = "/Resources/";

        public override string Id => GeneratorId;
        public override string DisplayName => "Resources Paths";

        // Not "Resources": that would shadow UnityEngine.Resources at the call site.
        protected override string DefaultClassName => "ResourcePaths";

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            var loadPaths = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;
                if (AssetDatabase.IsValidFolder(assetPath))
                    continue;

                var loadPath = ToResourcesPath(assetPath);
                if (loadPath == null || !seen.Add(loadPath))
                    continue;

                loadPaths.Add(loadPath);
            }

            // Deterministic order so unchanged Resources contents produce an identical file.
            loadPaths.Sort(StringComparer.Ordinal);

            foreach (var loadPath in loadPaths)
                rootMembers.Add(new ConstMember(loadPath, "string", Str(loadPath)));
        }

        public override bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
            => TouchesResources(importedAssets) || TouchesResources(deletedAssets) || TouchesResources(movedAssets);

        /// <summary>
        /// Maps an asset path to the key <c>Resources.Load</c> expects (relative to the nearest
        /// enclosing Resources folder, extension stripped), or null if the asset is not under a
        /// Resources folder. Pure string logic — unit-testable without asset state.
        /// </summary>
        public static string? ToResourcesPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            // Unity treats the deepest "Resources" folder in the path as the load root.
            var idx = assetPath.LastIndexOf(Marker, StringComparison.Ordinal);
            if (idx < 0)
                return null;

            var relative = assetPath.Substring(idx + Marker.Length);

            var dot = relative.LastIndexOf('.');
            if (dot >= 0)
                relative = relative.Substring(0, dot);

            return relative.Length == 0 ? null : relative;
        }

        private static bool TouchesResources(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.StartsWith("Assets/", StringComparison.Ordinal) &&
                    path.Contains(Marker, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
