using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Project-wide console settings, edited in Project Settings > Dev Console. They are kept as JSON in a
    /// <c>Resources</c> folder (<c>Assets/Resources/DevConsoleSettings.json</c> unless moved), so a player build carries
    /// them; without the file every setting has its default.
    /// </summary>
    public sealed class DevConsoleSettings : ScriptableObject
    {
        /// <summary>The name the settings file is loaded by from any <c>Resources</c> folder.</summary>
        public const string ResourceName = "DevConsoleSettings";

        [SerializeField] private bool useBuiltInToggle = true;
        [SerializeField] private Key toggleKey = Key.Backquote;
        [Range(0.1f, 0.9f)]
        [SerializeField] private float consoleHeight = 0.4f;

        public bool UseBuiltInToggle => useBuiltInToggle;
        public Key ToggleKey => toggleKey;
        public float ConsoleHeight => consoleHeight;

        private static DevConsoleSettings? _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => _instance = null;

        /// <summary>The settings, read from <c>Resources</c> the first time they are asked for.</summary>
        public static DevConsoleSettings GetOrCreate()
        {
            if (_instance != null) return _instance;

            var file = Resources.Load<TextAsset>(ResourceName);
            _instance = FromJson(file != null ? file.text : null);
            if (file != null) Resources.UnloadAsset(file);
            return _instance;
        }

        /// <summary>Settings with the values <paramref name="json"/> gives and defaults for the rest.</summary>
        internal static DevConsoleSettings FromJson(string? json)
        {
            var settings = CreateInstance<DevConsoleSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            if (string.IsNullOrWhiteSpace(json)) return settings;

            try
            {
                JsonUtility.FromJsonOverwrite(json, settings);
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"[DevConsole] {ResourceName}.json could not be read, using the defaults: {e.Message}");
            }

            return settings;
        }

        /// <summary>The settings as the JSON they are stored as.</summary>
        public string ToJson() => JsonUtility.ToJson(this, true);
    }
}
