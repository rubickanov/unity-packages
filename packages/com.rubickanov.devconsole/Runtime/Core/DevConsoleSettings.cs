using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole
{
    public class DevConsoleSettings : ScriptableObject
    {
        private const string SettingsPath = "ProjectSettings/DevConsoleSettings.json";

        [SerializeField] private bool useBuiltInToggle = true;
        [SerializeField] private Key toggleKey = Key.Backquote;
        [Range(0.1f, 0.9f)]
        [SerializeField] private float consoleHeight = 0.4f;

        public bool UseBuiltInToggle => useBuiltInToggle;
        public Key ToggleKey => toggleKey;
        public float ConsoleHeight => consoleHeight;

        private static DevConsoleSettings _instance;

        public static DevConsoleSettings GetOrCreate()
        {
            if (_instance != null) return _instance;
            _instance = CreateInstance<DevConsoleSettings>();
            if (File.Exists(SettingsPath))
                JsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), _instance);
            _instance.hideFlags = HideFlags.HideAndDontSave;
            return _instance;
        }

        public void Save()
        {
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
        }
    }
}
