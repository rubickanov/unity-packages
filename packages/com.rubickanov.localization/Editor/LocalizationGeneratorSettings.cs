using UnityEditor;
using UnityEngine;

namespace Rubickanov.Localization.Editor
{
    /// <summary>
    /// Settings for the localization keys generator.
    /// Stored in ProjectSettings/LocalizationGeneratorSettings.asset
    /// </summary>
    [FilePath("ProjectSettings/LocalizationGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class LocalizationGeneratorSettings : ScriptableSingleton<LocalizationGeneratorSettings>
    {
        [SerializeField] private string _outputPath = "Assets/Code/Game/Localization/LocalizationKeys.Generated.cs";
        [SerializeField] private string _namespace = "Game.Localization";
        [SerializeField] private string _className = "L";
        [SerializeField] private bool _autoRegenerate = true;

        /// <summary>Output file path for the generated keys class.</summary>
        public string OutputPath => _outputPath;

        /// <summary>C# namespace for the generated class.</summary>
        public string Namespace => _namespace;

        /// <summary>Name of the generated static class (e.g. "L").</summary>
        public string ClassName => _className;

        /// <summary>Whether to auto-regenerate when String Tables change.</summary>
        public bool AutoRegenerate => _autoRegenerate;

        /// <summary>Saves settings to disk.</summary>
        public void Save() => Save(true);
    }

    /// <summary>
    /// Project Settings provider for <see cref="LocalizationGeneratorSettings"/>.
    /// Accessible via Project Settings / Localization Generator.
    /// </summary>
    public class LocalizationGeneratorSettingsProvider : SettingsProvider
    {
        private SerializedObject? _serializedObject;

        public LocalizationGeneratorSettingsProvider()
            : base("Project/Localization Generator", SettingsScope.Project) { }

        public override void OnGUI(string searchContext)
        {
            _serializedObject ??= new SerializedObject(LocalizationGeneratorSettings.instance);
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
                LocalizationGeneratorSettings.instance.Save();
            }

            var outputPath = LocalizationGeneratorSettings.instance.OutputPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                EditorGUILayout.HelpBox(
                    "Output Path is empty. Generation will fail until a valid path is set.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(20);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(outputPath)))
            {
                if (GUILayout.Button("Generate Keys", GUILayout.Height(25)))
                {
                    LocalizationKeysGenerator.GenerateKeys();
                }
            }
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new LocalizationGeneratorSettingsProvider();
    }
}
