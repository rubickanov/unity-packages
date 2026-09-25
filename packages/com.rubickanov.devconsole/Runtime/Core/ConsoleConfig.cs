using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Keeps aliases and key bindings in <c>persistentDataPath/console/config.cfg</c>, one console command per line
    /// (<c>alias set slow timescale 0.2</c>, <c>bind set F5 "timescale 0.5; clear"</c>). The file is rewritten on every
    /// change and read back when the registries are first used, so it can be opened, edited or copied to another machine
    /// by hand. Commands of your own belong in <c>autoexec.cfg</c> beside it, which runs when the console initializes.
    /// </summary>
    public static class ConsoleConfig
    {
        public const string ConfigFileName = "config.cfg";
        public const string AutoexecFileName = "autoexec.cfg";

        private const string AliasPrefsKey = "DevConsole_Aliases";
        private const string BindingsPrefsKey = "DevConsole_Bindings";

        /// <summary>Folder holding the config, <c>persistentDataPath/console</c> unless a test points it elsewhere.</summary>
        public static string Directory => DirectoryOverride ?? Path.Combine(Application.persistentDataPath, "console");

        /// <summary>Full path of <c>config.cfg</c>.</summary>
        public static string ConfigPath => Path.Combine(Directory, ConfigFileName);

        internal static string? DirectoryOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => DirectoryOverride = null;

        /// <summary>The alias lines of the config, or the aliases of a pre-2.0 PlayerPrefs save when there is no config yet.</summary>
        internal static void ReadAliases(Dictionary<string, string> into)
        {
            if (File.Exists(ConfigPath))
            {
                foreach (var (name, command) in Read("alias"))
                    if (AliasRegistry.IsValidName(name)) into[name.ToLowerInvariant()] = command;
                return;
            }

            foreach (var (name, command) in ReadLegacy(AliasPrefsKey))
                into[name] = command;
        }

        /// <summary>The binding lines of the config, or the bindings of a pre-2.0 PlayerPrefs save when there is no config yet.</summary>
        internal static void ReadBindings(Dictionary<KeyChord, string> into)
        {
            var entries = File.Exists(ConfigPath) ? Read("bind") : ReadLegacy(BindingsPrefsKey);
            foreach (var (key, command) in entries)
                if (KeyChord.TryParse(key, out var chord)) into[chord] = command;
        }

        /// <summary>Rewrites the config with the given aliases and bindings.</summary>
        internal static void Write(AliasRegistry aliases, BindingRegistry bindings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Written by the dev console: `alias set` and `bind set` rewrite this file.");
            sb.AppendLine($"# Commands of your own go in {AutoexecFileName} next to it.");

            var names = aliases.SortedNames;
            for (int i = 0; i < names.Count; i++)
                AppendLine(sb, "alias", names[i], aliases.Aliases[names[i]]);

            var keys = new List<KeyChord>(bindings.Bindings.Keys);
            keys.Sort((a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase));
            foreach (var key in keys)
                AppendLine(sb, "bind", key.ToString(), bindings.Bindings[key]);

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevConsole] Failed to write {ConfigPath}: {e.Message}");
                return;
            }

            // The config holds everything now, so the pre-2.0 copies must not come back if it is ever deleted
            if (PlayerPrefs.HasKey(AliasPrefsKey) || PlayerPrefs.HasKey(BindingsPrefsKey))
            {
                PlayerPrefs.DeleteKey(AliasPrefsKey);
                PlayerPrefs.DeleteKey(BindingsPrefsKey);
                PlayerPrefs.Save();
            }
        }

        private static void AppendLine(StringBuilder sb, string kind, string name, string command)
        {
            sb.Append(kind).Append(" set ").Append(name).Append(' ');

            // Written the way it would be typed. A chain is quoted so that reading the line back keeps it one command;
            // quotes inside such a chain cannot be written, as the console has no escape for them.
            if (command.IndexOf(';') >= 0)
            {
                if (command.IndexOf('"') >= 0)
                    Debug.LogWarning($"[DevConsole] {kind} '{name}' mixes ';' and quotes and will not read back intact.");
                sb.Append('"').Append(command).Append('"');
            }
            else
            {
                sb.Append(command);
            }

            sb.AppendLine();
        }

        // Lines of the form `<kind> set <name> <command...>`, read the way the console would read them
        private static List<(string name, string command)> Read(string kind)
        {
            var entries = new List<(string, string)>();
            string[] lines;
            try
            {
                lines = File.ReadAllLines(ConfigPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevConsole] Failed to read {ConfigPath}: {e.Message}");
                return entries;
            }

            foreach (var raw in lines)
            {
                var text = raw.Trim();
                if (text.Length == 0 || text[0] == '#') continue;

                var line = new CommandLine(text);
                if (line.Count < 4 || !string.Equals(line.Tokens[0], kind, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(line.Tokens[1], "set", StringComparison.OrdinalIgnoreCase))
                    continue;

                entries.Add((line.Tokens[2], line.Remainder(3)));
            }

            return entries;
        }

        private static List<(string name, string command)> ReadLegacy(string prefsKey)
        {
            var entries = new List<(string, string)>();
            if (!PlayerPrefs.HasKey(prefsKey)) return entries;
            try
            {
                var data = JsonUtility.FromJson<LegacyData>(PlayerPrefs.GetString(prefsKey));
                if (data?.keys == null || data.values == null) return entries;
                for (int i = 0; i < data.keys.Count && i < data.values.Count; i++)
                    entries.Add((data.keys[i], data.values[i]));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevConsole] Failed to read saved {prefsKey}: {e.Message}");
            }

            return entries;
        }

        [Serializable]
        private class LegacyData
        {
            public List<string> keys = new();
            public List<string> values = new();
        }
    }
}
