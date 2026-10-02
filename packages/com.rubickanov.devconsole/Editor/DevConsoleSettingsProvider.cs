using UnityEditor;
using UnityEngine;

namespace Rubickanov.DevConsole.Editor
{
    public class DevConsoleSettingsProvider : SettingsProvider
    {
        private SerializedObject _serializedSettings;
        private DevConsoleSettings _settings;
        private string _savedTo;

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
            _savedTo = DevConsoleSettingsStore.FindAssetPath();
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
                    "Enable to use a keyboard key. Disable to open and close it yourself with DevConsoleWindow.Instance.Toggle()."));

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
                    "Call DevConsoleWindow.Instance.Toggle() or SetOpen(bool) from your input system.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("consoleHeight"),
                new GUIContent("Console Height", "Height as fraction of screen."));

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("createWindow"),
                new GUIContent("Create Window",
                    "Create the console window before the first scene loads. Off: call DevConsoleWindow.Create() yourself."));

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("runStartupCommands"),
                new GUIContent("Run Startup Commands",
                    "Run the -command lines on the first frame, after autoexec. Off: call StartupCommands.Run(CommandRegistry.Instance) when your commands are registered."));

            if (_serializedSettings.ApplyModifiedProperties())
            {
                DevConsoleSettingsStore.Save(_settings);
                _savedTo = DevConsoleSettingsStore.FindAssetPath();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                _savedTo != null
                    ? $"Saved to {_savedTo}, which builds carry."
                    : $"Every setting has its default. A change creates {DevConsoleSettingsStore.DefaultAssetPath}, which builds carry.",
                MessageType.None);
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new DevConsoleSettingsProvider();
    }
}
