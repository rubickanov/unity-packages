using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Rubickanov.DevConsole.Commands
{
    internal static class ConsoleCommands
    {
        private const int MaxRepeat = 1000;
        private const int MaxWaitFrames = 100000;

        // Which value each toggle ran last, keyed by its whole argument list
        private static readonly Dictionary<string, int> ToggleStates = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ToggleStates.Clear();

        /// <summary>Registers the <c>alias</c>, <c>bind</c> and <c>history</c> groups.</summary>
        internal static void Register(CommandRegistry registry)
        {
            registry.Group("alias", "Short names for commands", "Console", g => g
                .Add("list", ListAliases, "Show all aliases")
                .AddWithRest("set", 1, SetAlias(registry),
                    "Create or replace an alias; $1..$9 and $* take the call's arguments",
                    "<name> <command...>", null, CommandLineProvider.Instance)
                .Add("remove", RemoveAlias, "Remove an alias", AliasNameProvider.Instance)
                .Add("clear", ClearAliases, "Remove all aliases"));

            registry.Group("bind", "Run commands on key presses", "Console", g => g
                .Add("list", ListBindings, "Show all bindings")
                .AddWithRest("set", 1, SetBinding,
                    "Bind a key, with optional ctrl+/shift+/alt+, to a command",
                    "<key> <command...>", new KeyChordProvider(), CommandLineProvider.Instance)
                .Add("remove", RemoveBinding, "Remove a binding", BoundKeyProvider.Instance)
                .Add("clear", ClearBindings, "Remove all bindings"));

            // A registered command rather than syntax, so a PreExecuteFilter can refuse it: the netcode bridge does for
            // commands sent by a client, whose deferred rest would otherwise run later without that client's checks
            registry.Register("wait", args =>
            {
                if (args.Length != 1 || !int.TryParse(args[0], out var frames) || frames < 1 || frames > MaxWaitFrames)
                    throw new CommandException($"Usage: wait <frames>, 1..{MaxWaitFrames}");
                registry.PendingWait = frames;
                return null;
            }, "Delay the rest of a ; chain or exec file by N frames", "Console");

            registry.Register("toggle", Toggle(registry),
                "Run a command with the next of its values each time: toggle timescale 0 1", "Console",
                new IAutoCompleteProvider?[] { CommandLineProvider.Instance });

            registry.Group("history", "Previously entered commands", "Console", g => g
                .Add("list", ListHistory, "Show the last N commands, all when N is left out")
                .Add("clear", ClearHistory, "Remove all history entries"));
        }

        // ── alias ────────────────────────────────────────────────────

        private static string? ListAliases(string[] args)
        {
            ExpectAtMost(args, 0, "alias list");
            var aliases = AliasRegistry.Instance;
            if (aliases.Aliases.Count == 0) return "No aliases defined.";

            var names = aliases.SortedNames;
            for (int i = 0; i < names.Count; i++)
                ConsoleLog.Log($"  {names[i]} → {aliases.Aliases[names[i]]}");
            return null;
        }

        private static System.Func<string[], string?> SetAlias(CommandRegistry registry) => args =>
        {
            if (args.Length < 2) throw new CommandException("Usage: alias set <name> <command...>");

            var name = args[0];
            if (!AliasRegistry.IsValidName(name))
                throw new CommandException($"'{name}' cannot be an alias name: no spaces, quotes, ';' or '$'.");
            // Commands are looked up before aliases, so an alias named like one would never run
            if (registry.Commands.ContainsKey(name.ToLowerInvariant()))
                throw new CommandException($"'{name}' is already a command, an alias with that name would never run.");

            AliasRegistry.Instance.Set(name, args[1]);
            ConsoleLog.LogSuccess($"Alias '{name.ToLowerInvariant()}' → '{args[1]}'");
            return null;
        };

        private static string? RemoveAlias(string[] args)
        {
            ExpectExactly(args, 1, "alias remove <name>");
            if (!AliasRegistry.Instance.Remove(args[0]))
                throw new CommandException($"Alias '{args[0]}' not found.");
            ConsoleLog.LogSuccess($"Alias '{args[0]}' removed.");
            return null;
        }

        private static string? ClearAliases(string[] args)
        {
            ExpectAtMost(args, 0, "alias clear");
            AliasRegistry.Instance.Clear();
            ConsoleLog.LogSuccess("All aliases cleared.");
            return null;
        }

        // ── bind ─────────────────────────────────────────────────────

        private static string? ListBindings(string[] args)
        {
            ExpectAtMost(args, 0, "bind list");
            var bindings = BindingRegistry.Instance.Bindings;
            if (bindings.Count == 0) return "No key bindings defined.";

            var keys = new List<string>(bindings.Count);
            var byName = new Dictionary<string, string>(bindings.Count);
            foreach (var kvp in bindings)
            {
                var key = kvp.Key.ToString();
                keys.Add(key);
                byName[key] = kvp.Value;
            }

            keys.Sort(System.StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
                ConsoleLog.Log($"  {key} → {byName[key]}");
            return null;
        }

        private static string? SetBinding(string[] args)
        {
            if (args.Length < 2) throw new CommandException("Usage: bind set <key> <command...>");

            var chord = ParseChord(args[0]);
            var settings = DevConsoleSettings.GetOrCreate();
            if (settings.UseBuiltInToggle && chord.Key == settings.ToggleKey)
                ConsoleLog.LogWarning($"{settings.ToggleKey} also opens the console.");

            BindingRegistry.Instance.Set(chord, args[1]);
            // Created now and not only at startup, so a binding made in a session with none works straight away
            if (Application.isPlaying) CommandBindings.EnsureExists();
            ConsoleLog.LogSuccess($"Bound {chord} → '{args[1]}'");
            return null;
        }

        private static string? RemoveBinding(string[] args)
        {
            ExpectExactly(args, 1, "bind remove <key>");
            var chord = ParseChord(args[0]);
            if (!BindingRegistry.Instance.Remove(chord))
                throw new CommandException($"No binding for {chord}.");
            ConsoleLog.LogSuccess($"Unbound {chord}.");
            return null;
        }

        private static string? ClearBindings(string[] args)
        {
            ExpectAtMost(args, 0, "bind clear");
            BindingRegistry.Instance.Clear();
            ConsoleLog.LogSuccess("All key bindings cleared.");
            return null;
        }

        private static KeyChord ParseChord(string text)
        {
            if (!KeyChord.TryParse(text, out var chord))
                throw new CommandException(
                    $"Unknown key '{text}'. Use InputSystem key names (F5, Space, Digit1), optionally after ctrl+, shift+ or alt+.");
            return chord;
        }

        // ── toggle ───────────────────────────────────────────────────

        private static System.Func<string[], string?> Toggle(CommandRegistry registry) => args =>
        {
            if (args.Length < 3) throw new CommandException("Usage: toggle <command> <value> <value>...");

            var key = string.Join("\u0001", args);
            var next = ToggleStates.TryGetValue(key, out var last) ? (last + 1) % (args.Length - 1) : 0;
            ToggleStates[key] = next;

            // A value that was quoted is quoted again, so it reaches the command as one argument
            var value = args[next + 1];
            if (value.IndexOf(' ') >= 0) value = "\"" + value + "\"";

            var result = registry.Execute(args[0] + " " + value);
            if (!result.Success) throw new CommandException(result.Message ?? "failed");
            return result.Message;
        };

        // ── history ──────────────────────────────────────────────────

        private static string? ListHistory(string[] args)
        {
            ExpectAtMost(args, 1, "history list [N]");
            var history = CommandHistory.Current;
            if (history == null) throw new CommandException("Command history not available.");

            var entries = history.Entries;
            if (entries.Count == 0) return "History is empty.";

            var first = 0;
            if (args.Length == 1)
            {
                if (!int.TryParse(args[0], out var count) || count <= 0)
                    throw new CommandException($"'{args[0]}' is not a positive number.");
                first = Mathf.Max(0, entries.Count - count);
            }

            for (int i = first; i < entries.Count; i++)
                ConsoleLog.Log($"  {i}: {entries[i]}");
            return null;
        }

        private static string? ClearHistory(string[] args)
        {
            ExpectAtMost(args, 0, "history clear");
            var history = CommandHistory.Current;
            if (history == null) throw new CommandException("Command history not available.");
            history.Clear();
            ConsoleLog.LogSuccess("Command history cleared.");
            return null;
        }

        private static void ExpectExactly(string[] args, int count, string usage)
        {
            if (args.Length != count) throw new CommandException($"Usage: {usage}");
        }

        private static void ExpectAtMost(string[] args, int count, string usage)
        {
            if (args.Length > count) throw new CommandException($"Usage: {usage}");
        }

        // ── exec / repeat ────────────────────────────────────────────

        [ConsoleCommand("exec", "Run a file of commands from persistentDataPath/console/ or StreamingAssets/console/; .cfg may be left out", "Console")]
        public static void Exec(string filename)
        {
            if (!RunFile(CommandRegistry.Instance, filename, out var executed))
                throw new CommandException(
                    $"File not found: '{filename}' (searched persistentDataPath/console/ and StreamingAssets/console/)");
            ConsoleLog.LogSuccess($"Executed {executed} command(s) from '{filename}'.");
        }

        /// <summary>Runs <c>autoexec.cfg</c> if there is one. Called once the registry has its commands.</summary>
        internal static void RunAutoexec(CommandRegistry registry)
        {
            try
            {
                if (RunFile(registry, ConsoleConfig.AutoexecFileName, out var executed) && executed > 0)
                    Debug.Log($"[DevConsole] Ran {executed} command(s) from {ConsoleConfig.AutoexecFileName}.");
            }
            catch (CommandException e)
            {
                Debug.LogWarning($"[DevConsole] {ConsoleConfig.AutoexecFileName}: {e.Message}");
            }
        }

        private const int MaxExecDepth = 8;
        private static int _execDepth;

        /// <summary>
        /// Runs each line of the file through <paramref name="registry"/>, skipping blank lines and # comments. The
        /// player's own folder is searched before the shipped one, so a file there overrides a shipped one. False when
        /// the file does not exist.
        /// </summary>
        internal static bool RunFile(CommandRegistry registry, string filename, out int executed)
        {
            executed = 0;
            var lines = ReadScript(filename);
            if (lines == null) return false;

            // A file that execs itself, directly or through another, would otherwise overflow the stack
            if (_execDepth >= MaxExecDepth)
                throw new CommandException($"exec nested deeper than {MaxExecDepth} files, stopped at '{filename}'.");

            _execDepth++;
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                    registry.ExecuteAndLog(trimmed);
                    executed++;

                    // A line ending in `wait` defers the rest of the file
                    if (registry.PendingWait is { } frames)
                    {
                        registry.PendingWait = null;
                        var rest = new List<string>();
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            var next = lines[j].Trim();
                            if (next.Length > 0 && next[0] != '#') rest.Add(next);
                        }

                        if (rest.Count > 0) DeferredCommands.Schedule(registry, string.Join("; ", rest), frames);
                        break;
                    }
                }
            }
            finally
            {
                _execDepth--;
            }

            return true;
        }

        private static string[]? ReadScript(string filename)
        {
            var names = Path.HasExtension(filename) ? new[] { filename } : new[] { filename, filename + ".cfg" };
            var folders = new[]
            {
                ConsoleConfig.Directory,
                Path.Combine(Application.streamingAssetsPath, "console")
            };

            foreach (var folder in folders)
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                var lines = path.Contains("://") ? ReadPackedFile(path) : File.Exists(path) ? File.ReadAllLines(path) : null;
                if (lines != null) return lines;
            }

            return null;
        }

        // StreamingAssets inside an Android apk or on a WebGL server is a URL, which File cannot open
        private static string[]? ReadPackedFile(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser cannot wait for a download inside a frame
            throw new CommandException("exec cannot read StreamingAssets on WebGL; put the file in persistentDataPath/console/.");
#else
            using var request = UnityEngine.Networking.UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();
            while (!operation.isDone) { }
            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success) return null;
            return request.downloadHandler.text.Split('\n');
#endif
        }

        [ConsoleCommand("repeat", "Execute a command N times (at most 1000)", "Console")]
        [AutoComplete(1, typeof(CommandLineProvider))]
        public static void Repeat(int count, [Remainder] string command)
        {
            if (count < 0 || count > MaxRepeat)
                throw new CommandException($"Count must be between 0 and {MaxRepeat}.");

            for (int i = 0; i < count; i++)
            {
                var result = CommandRegistry.Instance.Execute(command);
                if (!string.IsNullOrEmpty(result.Message))
                {
                    if (result.Success)
                        ConsoleLog.Log(result.Message!);
                    else
                        ConsoleLog.LogError(result.Message!);
                }
            }
        }
    }
}
