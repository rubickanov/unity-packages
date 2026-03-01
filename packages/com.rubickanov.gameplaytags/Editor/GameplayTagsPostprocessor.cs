using UnityEditor;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Auto-regenerates gameplay tag constants when tag database assets are imported or deleted.
    /// </summary>
    public class GameplayTagsPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!GameplayTagsGeneratorSettings.instance.AutoRegenerate)
                return;

            var shouldRegenerate = false;

            foreach (var path in importedAssets)
            {
                if (IsTagAsset(path, isDeleted: false))
                {
                    shouldRegenerate = true;
                    break;
                }
            }

            if (!shouldRegenerate)
            {
                foreach (var path in deletedAssets)
                {
                    if (IsTagAsset(path, isDeleted: true))
                    {
                        shouldRegenerate = true;
                        break;
                    }
                }
            }

            if (shouldRegenerate)
            {
                EditorApplication.delayCall += GameplayTagsGenerator.GenerateTags;
            }
        }

        private static bool IsTagAsset(string path, bool isDeleted)
        {
            if (!path.EndsWith(".asset"))
                return false;

            if (isDeleted)
                return path.Contains("GameplayTag");

            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(GameplayTagAsset);
        }
    }
}
