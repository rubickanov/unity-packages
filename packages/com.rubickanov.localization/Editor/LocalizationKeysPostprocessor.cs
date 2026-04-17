using UnityEditor;
using UnityEditor.Localization;

namespace Rubickanov.Localization.Editor
{
    /// <summary>
    /// Auto-regenerates localization keys when String Tables are modified.
    /// </summary>
    public class LocalizationKeysPostprocessor : AssetPostprocessor
    {
        private static bool _pendingGeneration;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!LocalizationGeneratorSettings.instance.AutoRegenerate)
                return;

            var shouldRegenerate = false;

            foreach (var path in importedAssets)
            {
                if (IsStringTableAsset(path))
                {
                    shouldRegenerate = true;
                    break;
                }
            }

            if (!shouldRegenerate)
            {
                foreach (var path in deletedAssets)
                {
                    if (IsDeletedStringTableAsset(path))
                    {
                        shouldRegenerate = true;
                        break;
                    }
                }
            }

            if (shouldRegenerate && !_pendingGeneration)
            {
                _pendingGeneration = true;
                EditorApplication.delayCall += RunGeneration;
            }
        }

        private static void RunGeneration()
        {
            _pendingGeneration = false;
            LocalizationKeysGenerator.GenerateKeys();
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

        // For deleted assets the type is no longer resolvable via AssetDatabase — fall back
        // to path heuristics. Best-effort: may over-trigger, but regeneration is idempotent.
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
