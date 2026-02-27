using UnityEditor;
using UnityEngine;

namespace Rubickanov.DevConsole.Editor
{
    public class DevConsoleSettingsProvider : SettingsProvider
    {
        private SerializedObject _serializedSettings;
        private DevConsoleSettings _settings;

        private DevConsoleSettingsProvider()
            : base("Project/Dev Console", SettingsScope.Project)
        {
            label = "Dev Console";
            keywords = new[] { "console", "dev", "debug", "cheat", "commands" };
        }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            _settings = DevConsoleSettings.GetOrCreate();
            _serializedSettings = new SerializedObject(_settings);
        }

        public override void OnGUI(string searchContext)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                _settings = DevConsoleSettings.GetOrCreate();
                _serializedSettings = new SerializedObject(_settings);
            }

            _serializedSettings.Update();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("useBuiltInToggle"),
                new GUIContent("Use Built-in Toggle",
                    "Enable to use a keyboard key. Disable to control via DevConsoleUI.Instance.Toggle()."));

            if (_serializedSettings.FindProperty("useBuiltInToggle").boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    _serializedSettings.FindProperty("toggleKey"),
                    new GUIContent("Toggle Key"));
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Call DevConsoleUI.Instance.Toggle() or .Show(bool) from your input system.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("consoleHeight"),
                new GUIContent("Console Height", "Height as fraction of screen."));

            if (_serializedSettings.ApplyModifiedProperties())
                _settings.Save();
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new DevConsoleSettingsProvider();
    }
}
