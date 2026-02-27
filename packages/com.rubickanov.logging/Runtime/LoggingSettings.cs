using Microsoft.Extensions.Logging;
using UnityEngine;

namespace Rubickanov.Logging
{
    /// <summary>
    /// Project-wide logging settings stored as a preloaded asset.
    /// Accessible via Edit > Project Settings > Logging.
    /// </summary>
    public class LoggingSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/Settings/LoggingSettings.asset";

        [field: SerializeField] public LogLevel MinimumLevel { get; private set; } = LogLevel.Debug;
        [field: SerializeField] public string LogDirectoryName { get; private set; } = "Logs";
        [field: SerializeField] public int MaxLogFiles { get; private set; } = 5;
        [field: SerializeField] public string FilePrefix { get; private set; } = "game";
        [field: SerializeField] public string TimestampFormat { get; private set; } = "yyyy-MM-dd_HH-mm-ss";
        [field: SerializeField] public bool PrettyStacktrace { get; private set; }

        private static LoggingSettings? _instance;

        public static LoggingSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;
#if UNITY_EDITOR
                _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<LoggingSettings>(AssetPath);
#else
                var found = Resources.FindObjectsOfTypeAll<LoggingSettings>();
                if (found.Length > 0) _instance = found[0];
#endif
                if (_instance == null)
                    _instance = CreateInstance<LoggingSettings>();
                return _instance;
            }
        }
    }
}
