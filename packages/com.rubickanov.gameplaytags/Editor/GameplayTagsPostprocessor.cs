using UnityEditor;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Auto-regenerates gameplay tag constants when tag database assets are imported or deleted.
    /// Coalesces rapid-fire import events so the generator runs at most once per frame.
    /// </summary>
    public class GameplayTagsPostprocessor : AssetPostprocessor
    {
        private static bool _pendingRegeneration;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!GameplayTagsGeneratorSettings.instance.AutoRegenerate)
                return;

            if (_pendingRegeneration)
                return;

            var shouldRegenerate = false;

            foreach (var path in importedAssets)
            {
                if (IsTagAssetPath(path))
                {
                    shouldRegenerate = true;
                    break;
                }
            }

            if (!shouldRegenerate)
            {
                foreach (var path in deletedAssets)
                {
                    // For deleted assets we cannot query the type anymore; regenerate on any
                    // .asset removal. The generator is cheap enough that a false positive is fine.
                    if (path.EndsWith(".asset"))
                    {
                        shouldRegenerate = true;
                        break;
                    }
                }
            }

            if (shouldRegenerate)
            {
                _pendingRegeneration = true;
                EditorApplication.delayCall += RunGeneration;
            }
        }

        private static void RunGeneration()
        {
            _pendingRegeneration = false;
            GameplayTagsGenerator.GenerateTags();
        }

        private static bool IsTagAssetPath(string path)
        {
            if (!path.EndsWith(".asset"))
                return false;

            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(GameplayTagAsset);
        }
    }
}
