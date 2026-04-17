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
        public enum AccessModifierOption
        {
            Public,
            Internal,
        }

        [SerializeField] private string _outputPath = "Assets/Code/Game/Tags/GameTags.Generated.cs";
        [SerializeField] private string _namespace = "Game.Tags";
        [SerializeField] private string _className = "GameTags";
        [SerializeField] private bool _autoRegenerate = true;
        [SerializeField] private AccessModifierOption _accessModifier = AccessModifierOption.Public;
        [SerializeField] private bool _makePartial;

        /// <summary>Output file path for the generated constants class.</summary>
        public string OutputPath => _outputPath;

        /// <summary>C# namespace for the generated class.</summary>
        public string Namespace => _namespace;

        /// <summary>Name of the generated static class.</summary>
        public string ClassName => _className;

        /// <summary>Whether to auto-regenerate when the tag database changes.</summary>
        public bool AutoRegenerate => _autoRegenerate;

        /// <summary>Access modifier keyword ("public" or "internal") to use on the generated class and members.</summary>
        public string AccessModifier => _accessModifier == AccessModifierOption.Internal ? "internal" : "public";

        /// <summary>If true, the generated class is declared <c>partial</c> so users can extend it in other files.</summary>
        public bool MakePartial => _makePartial;

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
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("_accessModifier"), new GUIContent("Access Modifier"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("_makePartial"), new GUIContent("Partial Class"));

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
