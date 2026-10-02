using System.Collections.Generic;
using Rubickanov.Codegen.Editor;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace Rubickanov.Localization.Editor
{
    /// <summary>
    /// Registers the localization keys generator with the central codegen pipeline: scans String
    /// Table Collections, runs the pure <see cref="LocalizationKeysGenerator"/>, and writes the
    /// result through the shared idempotent file writer.
    /// </summary>
    public sealed class LocalizationCodeGenerator : ICodeGenerator
    {
        public const string GeneratorId = "localization";

        public string Id => GeneratorId;
        public string DisplayName => "Localization Keys";

        public GeneratorConfig CreateDefaultConfig() => new()
        {
            Id = Id,
            Enabled = true,
            AutoRegenerate = true,
            OutputPath = "Assets/Code/Game/Localization/LocalizationKeys.Generated.cs",
            Namespace = "Game.Localization",
            ClassName = "L",
            Access = GeneratedAccess.Public,
        };

        public void Generate(GeneratorConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.OutputPath))
            {
                Debug.LogError("[LocalizationKeysGenerator] OutputPath is empty. " +
                               "Configure it in Project Settings / Rubickanov Codegen.");
                return;
            }

            var tables = FindAllStringTableCollections();
            if (tables.Count == 0)
            {
                Debug.LogWarning("[LocalizationKeysGenerator] No String Table Collections found.");
                return;
            }

            var options = new LocalizationCodeOptions
            {
                Namespace = config.Namespace,
                ClassName = config.ClassName,
            };

            var code = LocalizationKeysGenerator.GenerateCode(tables, options);
            if (GeneratedFileWriter.Write(config.OutputPath, code))
                Debug.Log($"[LocalizationKeysGenerator] Generated {config.OutputPath} with {tables.Count} table(s).");
        }

        public bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
        {
            foreach (var path in importedAssets)
            {
                if (IsStringTableAsset(path))
                    return true;
            }

            foreach (var path in deletedAssets)
            {
                if (IsDeletedStringTableAsset(path))
                    return true;
            }

            return false;
        }

        private static Dictionary<string, List<string>> FindAllStringTableCollections()
        {
            var result = new Dictionary<string, List<string>>();

            var guids = AssetDatabase.FindAssets("t:StringTableCollection");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var collection = AssetDatabase.LoadAssetAtPath<StringTableCollection>(path);

                if (collection == null) continue;

                var tableName = collection.TableCollectionName;
                var keys = new List<string>();

                var sharedData = collection.SharedData;
                if (sharedData != null)
                {
                    foreach (var entry in sharedData.Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.Key))
                            keys.Add(entry.Key);
                    }
                }

                if (keys.Count > 0)
                    result[tableName] = keys;
            }

            return result;
        }

        private static bool IsStringTableAsset(string path)
        {
            if (!path.EndsWith(".asset"))
                return false;

            var collection = AssetDatabase.LoadAssetAtPath<StringTableCollection>(path);
            if (collection != null)
                return true;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.SharedTableData>(path);
            return asset != null;
        }

        // For deleted assets the type is no longer resolvable via AssetDatabase — fall back to path
        // heuristics. Best-effort: may over-trigger, but regeneration is idempotent.
        private static bool IsDeletedStringTableAsset(string path)
        {
            if (!path.EndsWith(".asset"))
                return false;

            return path.Contains("Localization") ||
                   path.Contains("StringTable") ||
                   path.EndsWith(" Shared.asset");
        }
    }
}
