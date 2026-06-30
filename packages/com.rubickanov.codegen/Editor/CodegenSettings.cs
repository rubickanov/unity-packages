using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Codegen.Editor
{
    /// <summary>
    /// Single project-local store for every generator's configuration, keyed by generator id.
    /// Replaces the per-package settings assets the individual generators used to ship. Stored in
    /// <c>ProjectSettings/RubickanovCodegenSettings.asset</c>.
    /// </summary>
    [FilePath("ProjectSettings/RubickanovCodegenSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class CodegenSettings : ScriptableSingleton<CodegenSettings>
    {
        [SerializeField] private List<GeneratorConfig> _configs = new();

        /// <summary>
        /// Returns the stored config for <paramref name="generator"/>, creating and persisting one
        /// from the generator's defaults on first access.
        /// </summary>
        public GeneratorConfig GetOrCreate(ICodeGenerator generator)
        {
            foreach (var config in _configs)
            {
                if (config.Id == generator.Id)
                    return config;
            }

            var created = generator.CreateDefaultConfig();
            created.Id = generator.Id;
            _configs.Add(created);
            Save();
            return created;
        }

        /// <summary>Saves settings to disk.</summary>
        public void Save() => Save(true);
    }
}
