using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits name and build-index constants for the scenes enabled in Build Settings, so scenes can
    /// be loaded by a checked constant instead of a magic string.
    /// </summary>
    public sealed class ScenesGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "scenes";

        public override string Id => GeneratorId;
        public override string DisplayName => "Scenes";
        protected override string DefaultClassName => "Scenes";

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            var buildIndex = 0;
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path))
                    continue;

                var name = Path.GetFileNameWithoutExtension(scene.path);
                rootMembers.Add(new ConstMember(name, "string", Str(name)));
                rootMembers.Add(new ConstMember($"{name}BuildIndex", "int", buildIndex.ToString()));
                buildIndex++;
            }
        }
    }

    /// <summary>
    /// Regenerates the scenes constants when the Build Settings scene list changes — that edit is
    /// not an asset import, so it cannot be caught by the shared postprocessor.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScenesListAutoRegen
    {
        static ScenesListAutoRegen()
        {
            EditorBuildSettings.sceneListChanged += OnSceneListChanged;
        }

        private static void OnSceneListChanged()
        {
            var generator = CodeGeneratorRegistry.FindById(ScenesGenerator.GeneratorId);
            if (generator == null)
                return;

            var config = CodegenSettings.instance.GetOrCreate(generator);
            if (!config.Enabled || !config.AutoRegenerate || string.IsNullOrWhiteSpace(config.OutputPath))
                return;

            EditorApplication.delayCall += () => generator.Generate(config);
        }
    }
}
