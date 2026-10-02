using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Logging.Editor
{
    /// <summary>
    /// Project Settings provider for <see cref="LoggingSettings"/>.
    /// Accessible via Edit > Project Settings > Logging.
    /// Auto-creates the settings asset and adds it to preloaded assets.
    /// </summary>
    public class LoggingSettingsProvider : SettingsProvider
    {
        private const string EnableFileKey = "Logging.EnableFileInEditor";
        private SerializedObject? _serializedObject;

        public LoggingSettingsProvider()
            : base("Project/Logging", SettingsScope.Project) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LoggingSettings>(LoggingSettings.AssetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LoggingSettings>();
                var dir = System.IO.Path.GetDirectoryName(LoggingSettings.AssetPath);
                if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                    AssetDatabase.Refresh();
                }
                AssetDatabase.CreateAsset(settings, LoggingSettings.AssetPath);
                AssetDatabase.SaveAssets();
            }
            EnsurePreloadedAsset(settings);
            _serializedObject = new SerializedObject(settings);
        }

        public override void OnGUI(string searchContext)
        {
            if (_serializedObject == null) return;
            _serializedObject.Update();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Log Files", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("<MinimumLevel>k__BackingField"), new GUIContent("Minimum Level"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("<LogDirectoryName>k__BackingField"), new GUIContent("Log Directory Name"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("<MaxLogFiles>k__BackingField"), new GUIContent("Max Log Files"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("<FilePrefix>k__BackingField"), new GUIContent("File Prefix"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("<TimestampFormat>k__BackingField"), new GUIContent("Timestamp Format"));

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Display", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedObject.FindProperty("<PrettyStacktrace>k__BackingField"), new GUIContent("Pretty Stacktrace"));

            if (_serializedObject.ApplyModifiedProperties())
            {
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Editor", EditorStyles.boldLabel);
            bool current = EditorPrefs.GetBool(EnableFileKey, false);
            bool next = EditorGUILayout.Toggle("Enable File Logging in Editor", current);
            if (next != current)
                EditorPrefs.SetBool(EnableFileKey, next);

            EditorGUILayout.HelpBox(
                "When enabled, logs are written to a file in Application.persistentDataPath. " +
                "Takes effect on next Play Mode enter.",
                MessageType.Info);
        }

        private static void EnsurePreloadedAsset(LoggingSettings settings)
        {
            var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            if (preloaded.Contains(settings)) return;
            preloaded.Add(settings);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new LoggingSettingsProvider();
    }
}
