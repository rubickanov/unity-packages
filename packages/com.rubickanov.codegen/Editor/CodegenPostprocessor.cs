using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Codegen.Editor
{
    /// <summary>
    /// Single asset postprocessor driving auto-regeneration for every registered generator.
    /// Each enabled, auto-regenerating generator is asked whether the asset changes concern it;
    /// matches are coalesced and run once on the next editor tick so a batch import regenerates
    /// at most once per generator.
    /// </summary>
    public class CodegenPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> Pending = new(StringComparer.Ordinal);
        private static bool _scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (var generator in CodeGeneratorRegistry.All)
            {
                var config = CodegenSettings.instance.GetOrCreate(generator);
                if (!config.Enabled || !config.AutoRegenerate)
                    continue;
                if (string.IsNullOrWhiteSpace(config.OutputPath))
                    continue;

                if (generator.HandlesAssetChange(importedAssets, deletedAssets, movedAssets))
                    Pending.Add(generator.Id);
            }

            if (Pending.Count > 0 && !_scheduled)
            {
                _scheduled = true;
                EditorApplication.delayCall += Flush;
            }
        }

        private static void Flush()
        {
            _scheduled = false;

            var ids = new List<string>(Pending);
            Pending.Clear();

            foreach (var id in ids)
            {
                var generator = CodeGeneratorRegistry.FindById(id);
                if (generator == null)
                    continue;

                var config = CodegenSettings.instance.GetOrCreate(generator);
                if (!config.Enabled || string.IsNullOrWhiteSpace(config.OutputPath))
                    continue;

                try
                {
                    generator.Generate(config);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Codegen] Auto-regeneration of '{generator.DisplayName}' failed: {e}");
                }
            }
        }
    }
}
