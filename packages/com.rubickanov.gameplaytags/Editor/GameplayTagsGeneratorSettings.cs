using UnityEditor;
using UnityEngine;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Settings for the gameplay tags code generator.
    /// Stored in ProjectSettings/GameplayTagsGeneratorSettings.asset.
    /// </summary>
    [FilePath("ProjectSettings/GameplayTagsGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class GameplayTagsGeneratorSettings : ScriptableSingleton<GameplayTagsGeneratorSettings>
    {
        [SerializeField] private string _outputPath = "Assets/Code/Game/Tags/GameTags.Generated.cs";
        [SerializeField] private string _namespace = "Game.Tags";
        [SerializeField] private string _className = "GameTags";
        [SerializeField] private bool _autoRegenerate = true;

        /// <summary>Output file path for the generated constants class.</summary>
        public string OutputPath => _outputPath;

        /// <summary>C# namespace for the generated class.</summary>
        public string Namespace => _namespace;

        /// <summary>Name of the generated static class.</summary>
        public string ClassName => _className;

        /// <summary>Whether to auto-regenerate when the tag database changes.</summary>
        public bool AutoRegenerate => _autoRegenerate;

        /// <summary>Saves settings to disk.</summary>
        public void Save() => Save(true);
    }

    /// <summary>
    /// Project Settings provider for <see cref="GameplayTagsGeneratorSettings"/>.
    /// Accessible via Project Settings / Gameplay Tags Generator.
    /// </summary>
    public class GameplayTagsGeneratorSettingsProvider : SettingsProvider
    {
        private SerializedObject? _serializedObject;

        public GameplayTagsGeneratorSettingsProvider()
            : base("Project/Gameplay Tags Generator", SettingsScope.Project) { }

        public override void OnGUI(string searchContext)
        {
            _serializedObject ??= new SerializedObject(GameplayTagsGeneratorSettings.instance);
            _serializedObject.Update();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("_outputPath"), new GUIContent("Path"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("_namespace"), new GUIContent("Namespace"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("_className"), new GUIContent("Class Name"));

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("_autoRegenerate"), new GUIContent("Auto Regenerate"));

            if (_serializedObject.ApplyModifiedProperties())
            {
                GameplayTagsGeneratorSettings.instance.Save();
            }

            EditorGUILayout.Space(20);

            if (GUILayout.Button("Generate Tags", GUILayout.Height(25)))
            {
                GameplayTagsGenerator.GenerateTags();
            }
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new GameplayTagsGeneratorSettingsProvider();
    }
}
