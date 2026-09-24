using System.IO;
using System.Text;
using UnityEngine;

namespace Rubickanov.DevConsole.Commands
{
    internal static class LogCommands
    {
        [ConsoleCommand("log_unity", "Toggle forwarding Unity Debug.Log messages to the console (on by default)", "Logging")]
        public static void LogUnity(bool enabled = true)
        {
            if (enabled != UnityLogForwarder.Enabled)
            {
                UnityLogForwarder.Enabled = enabled;
                ConsoleLog.LogSuccess($"Unity log forwarding {(enabled ? "enabled" : "disabled")}.");
            }
            else
            {
                ConsoleLog.Log($"Unity log forwarding: {(enabled ? "ON" : "OFF")}");
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
    }
}
