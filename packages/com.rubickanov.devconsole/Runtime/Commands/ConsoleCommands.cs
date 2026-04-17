using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole.Commands
{
    internal static class ConsoleCommands
    {
        [ConsoleCommand("history", "Show command history", "Console")]
        public static void History()
        {
            var history = CommandHistory.Current;
            if (history == null)
            {
                ConsoleLog.LogError("Command history not available.");
                return;
            }

            var entries = history.Entries;
            if (entries.Count == 0)
            {
                ConsoleLog.Log("History is empty.");
                return;
            }

            for (int i = 0; i < entries.Count; i++)
                ConsoleLog.Log($"  {i}: {entries[i]}");
        }

        [ConsoleCommand("alias", "Manage command aliases (no args=list, 1 arg=show, 2 args=create)", "Console")]
        public static void Alias(string name = "", string command = "")
        {
            var registry = AliasRegistry.Instance;

            // No args: list all
            if (string.IsNullOrEmpty(name))
            {
                var aliases = registry.Aliases;
                if (aliases.Count == 0)
                {
                    ConsoleLog.Log("No aliases defined.");
                    return;
                }

                foreach (var kvp in aliases)
                    ConsoleLog.Log($"  {kvp.Key} → {kvp.Value}");
                return;
            }

            // 1 arg: show specific alias
            if (string.IsNullOrEmpty(command))
            {
                if (registry.TryResolve(name, out var resolved))
                    ConsoleLog.Log($"  {name} → {resolved}");
                else
                    ConsoleLog.LogError($"Alias '{name}' not found.");
                return;
            }

            // 2 args: create alias
            registry.Set(name, command);
            ConsoleLog.LogSuccess($"Alias '{name}' → '{command}'");
        }

        [ConsoleCommand("unalias", "Remove a command alias", "Console")]
        public static void Unalias(string name)
        {
            if (AliasRegistry.Instance.Remove(name))
                ConsoleLog.LogSuccess($"Alias '{name}' removed.");
            else
                ConsoleLog.LogError($"Alias '{name}' not found.");
        }

        [ConsoleCommand("alias_clear", "Remove all aliases", "Console")]
        public static void AliasClear()
        {
            AliasRegistry.Instance.Clear();
            ConsoleLog.LogSuccess("All aliases cleared.");
        }

        [ConsoleCommand("history_clear", "Remove all command history entries", "Console")]
        public static void HistoryClear()
        {
            var history = CommandHistory.Current;
            if (history == null)
            {
                ConsoleLog.LogError("Command history not available.");
                return;
            }
            history.Clear();
            ConsoleLog.LogSuccess("Command history cleared.");
        }

        [ConsoleCommand("bind", "Bind a key to a command (no args=list, 2 args=bind)", "Console")]
        public static void Bind(string key = "", string command = "")
        {
            // No args: list all
            if (string.IsNullOrEmpty(key))
            {
                var bindings = CommandBindings.GetInstance();
                if (bindings == null || bindings.Bindings.Count == 0)
                {
                    ConsoleLog.Log("No key bindings defined.");
                    return;
                }

                foreach (var kvp in bindings.Bindings)
                    ConsoleLog.Log($"  {kvp.Key} → {kvp.Value}");
                return;
            }

            if (string.IsNullOrEmpty(command))
            {
                ConsoleLog.LogError("Usage: bind <key> <command>");
                return;
            }

            if (!CommandBindings.TryParseKey(key, out var parsedKey))
            {
                ConsoleLog.LogError($"Unknown key '{key}'. Use InputSystem Key enum names (e.g. F5, Space, LeftShift).");
                return;
            }

            CommandBindings.EnsureExists().Bind(parsedKey, command);
            ConsoleLog.LogSuccess($"Bound {parsedKey} → '{command}'");
        }

        [ConsoleCommand("unbind", "Remove a key binding", "Console")]
        public static void Unbind(string key)
        {
            if (!CommandBindings.TryParseKey(key, out var parsedKey))
            {
                ConsoleLog.LogError($"Unknown key '{key}'.");
                return;
            }

            var bindings = CommandBindings.GetInstance();
            if (bindings != null && bindings.Unbind(parsedKey))
                ConsoleLog.LogSuccess($"Unbound {parsedKey}.");
            else
                ConsoleLog.LogError($"No binding for {parsedKey}.");
        }

        [ConsoleCommand("binding_clear", "Remove all key bindings", "Console")]
        public static void BindingClear()
        {
            var bindings = CommandBindings.GetInstance();
            if (bindings == null)
            {
                ConsoleLog.Log("No bindings to clear.");
                return;
            }
            bindings.Clear();
            ConsoleLog.LogSuccess("All key bindings cleared.");
        }

        [ConsoleCommand("exec", "Execute commands from a file in StreamingAssets/console/ or persistentDataPath/console/", "Console")]
        public static void Exec(string filename)
        {
            var streamingPath = Path.Combine(Application.streamingAssetsPath, "console", filename);
            var persistentPath = Path.Combine(Application.persistentDataPath, "console", filename);

            string? filePath = null;
            if (File.Exists(streamingPath))
                filePath = streamingPath;
            else if (File.Exists(persistentPath))
                filePath = persistentPath;

            if (filePath == null)
            {
                ConsoleLog.LogError($"File not found: '{filename}' (searched StreamingAssets/console/ and persistentDataPath/console/)");
                return;
            }

            var lines = File.ReadAllLines(filePath);
            int executed = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                ConsoleLog.LogInput(trimmed);
                var result = CommandRegistry.Instance.Execute(trimmed);
                if (!string.IsNullOrEmpty(result.Message))
                {
                    if (result.Success)
                        ConsoleLog.Log(result.Message);
                    else
                        ConsoleLog.LogError(result.Message);
                }
                executed++;
            }

            ConsoleLog.LogSuccess($"Executed {executed} command(s) from '{filename}'.");
        }

        [ConsoleCommand("repeat", "Execute a command N times", "Console")]
        public static void Repeat(int count, string command)
        {
            for (int i = 0; i < count; i++)
            {
                var result = CommandRegistry.Instance.Execute(command);
                if (!string.IsNullOrEmpty(result.Message))
                {
                    if (result.Success)
                        ConsoleLog.Log(result.Message);
                    else
                        ConsoleLog.LogError(result.Message);
                }
            }
        }
    }
}
