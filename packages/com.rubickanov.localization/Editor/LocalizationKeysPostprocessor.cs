using UnityEditor;

namespace Rubickanov.Localization.Editor
{
    /// <summary>
    /// Auto-regenerates localization keys when String Tables are modified.
    /// </summary>
    public class LocalizationKeysPostprocessor : AssetPostprocessor
    {
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
                    if (IsStringTableAsset(path))
                    {
                        shouldRegenerate = true;
                        break;
                    }
                }
            }

            if (shouldRegenerate)
            {
                EditorApplication.delayCall += LocalizationKeysGenerator.GenerateKeys;
            }
        }

        private static bool IsStringTableAsset(string path)
        {
            if (!path.EndsWith(".asset"))
                return false;

            return path.Contains("Localization") ||
                   path.Contains("StringTable") ||
                   path.EndsWith(" Shared.asset");
        }
    }
}
