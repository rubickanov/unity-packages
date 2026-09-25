using System.IO;
using System.Text;
using UnityEngine;

namespace Rubickanov.DevConsole.Commands
{
    internal static class LogCommands
    {
        [ConsoleCommand("log unity", "Show, or switch, copying of Unity Debug.Log messages into the console (on by default)", "Logging")]
        public static void LogUnity(bool? enabled = null)
        {
            if (enabled.HasValue && enabled.Value != UnityLogForwarder.Enabled)
            {
                UnityLogForwarder.Enabled = enabled.Value;
                ConsoleLog.LogSuccess($"Unity log forwarding {(enabled.Value ? "enabled" : "disabled")}.");
            }
            else
            {
                ConsoleLog.Log($"Unity log forwarding: {(UnityLogForwarder.Enabled ? "ON" : "OFF")}");
            }
        }

        [ConsoleCommand("log save", "Save console log to a file", "Logging")]
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
