using System.Collections.Generic;
using Rubickanov.Codegen.Editor;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Registers the gameplay tags generator with the central codegen pipeline: finds the tag
    /// database assets, runs the pure <see cref="GameplayTagsGenerator"/>, and writes the result
    /// through the shared idempotent file writer.
    /// </summary>
    public sealed class GameplayTagsCodeGenerator : ICodeGenerator
    {
        public const string GeneratorId = "gameplayTags";

        public string Id => GeneratorId;
        public string DisplayName => "Gameplay Tags";

        public GeneratorConfig CreateDefaultConfig() => new()
        {
            Id = Id,
            Enabled = true,
            AutoRegenerate = true,
            OutputPath = "Assets/Code/Game/Tags/GameTags.Generated.cs",
            Namespace = "Game.Tags",
            ClassName = "GameTags",
            Access = GeneratedAccess.Public,
            MakePartial = false,
        };

        public void Generate(GeneratorConfig config)
        {
            var assets = FindAllTagAssets();
            if (assets.Count == 0)
            {
                Debug.LogWarning("[GameplayTagsGenerator] No GameplayTagAsset found.");
                return;
            }

            var allPaths = new List<string>();
            var seen = new HashSet<string>();
            foreach (var asset in assets)
            {
                foreach (var path in asset.TagPaths)
                {
                    if (seen.Add(path))
                        allPaths.Add(path);
                }
            }

            var registry = new GameplayTagRegistry(allPaths.ToArray());
            var names = registry.GetAllNames();
            if (names.Count == 0)
            {
                Debug.LogWarning("[GameplayTagsGenerator] No tags found in database.");
                return;
            }

            var options = new GenerateCodeOptions
            {
                Namespace = config.Namespace,
                ClassName = config.ClassName,
                AccessModifier = config.AccessKeyword,
                MakePartial = config.MakePartial,
            };

            var code = GameplayTagsGenerator.GenerateCode(names, options);
            if (GeneratedFileWriter.Write(config.OutputPath, code))
                Debug.Log($"[GameplayTagsGenerator] Generated {config.OutputPath} with {names.Count} tag(s) from {assets.Count} asset(s).");
        }

        public bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
        {
            foreach (var path in importedAssets)
            {
                if (IsTagAssetPath(path))
                    return true;
            }

            // For deleted assets the type is no longer resolvable; regenerate on any .asset removal.
            // The generator is cheap and idempotent, so a false positive is harmless.
            foreach (var path in deletedAssets)
            {
                if (path.EndsWith(".asset"))
                    return true;
            }

            return false;
        }

        private static List<GameplayTagAsset> FindAllTagAssets()
        {
            var guids = AssetDatabase.FindAssets("t:GameplayTagAsset");
            var assets = new List<GameplayTagAsset>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameplayTagAsset>(path);
                if (asset != null)
                    assets.Add(asset);
            }

            return assets;
        }

        private static bool IsTagAssetPath(string path)
        {
            if (!path.EndsWith(".asset"))
                return false;

            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(GameplayTagAsset);
        }
    }
}
