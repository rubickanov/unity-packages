using System.IO;
using System.Text;
using UnityEngine;

namespace Rubickanov.DevConsole.Commands
{
    internal static class LogCommands
    {
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_subscribed)
            {
                Application.logMessageReceived -= OnUnityLogMessage;
                _subscribed = false;
            }
        }

        [ConsoleCommand("log_unity", "Toggle forwarding Unity Debug.Log messages to the console", "Logging")]
        public static void LogUnity(bool enabled = true)
        {
            if (enabled && !_subscribed)
            {
                Application.logMessageReceived += OnUnityLogMessage;
                _subscribed = true;
                ConsoleLog.LogSuccess("Unity log forwarding enabled.");
            }
            else if (!enabled && _subscribed)
            {
                Application.logMessageReceived -= OnUnityLogMessage;
                _subscribed = false;
                ConsoleLog.LogSuccess("Unity log forwarding disabled.");
            }
            else
            {
                ConsoleLog.Log($"Unity log forwarding: {(_subscribed ? "ON" : "OFF")}");
            }
        }

        [ConsoleCommand("log_save", "Save console log to a file", "Logging")]
        public static void LogSave(string path = "")
        {
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Application.persistentDataPath, "console_log.txt");

            var sb = new StringBuilder();
            foreach (var entry in ConsoleLog.Entries)
                sb.AppendLine($"[{entry.Timestamp:HH:mm:ss}] [{entry.Type}] {entry.Message}");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, sb.ToString());
            ConsoleLog.LogSuccess($"Log saved to: {path}");
        }

        private static void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    ConsoleLog.LogError($"[Unity] {condition}");
                    break;
                case LogType.Warning:
                    ConsoleLog.LogWarning($"[Unity] {condition}");
                    break;
                default:
                    ConsoleLog.Log($"[Unity] {condition}");
                    break;
            }
        }
    }
}
