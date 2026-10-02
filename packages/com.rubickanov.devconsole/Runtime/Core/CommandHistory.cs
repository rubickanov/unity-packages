using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// The lines typed into the console, oldest first, at most <see cref="MaxEntries"/>, with the Up/Down cursor that
    /// walks them. Kept in <c>history.txt</c> beside <c>config.cfg</c>, one line each, rewritten on every new line.
    /// One per <see cref="CommandRegistry"/>, as its <see cref="CommandRegistry.History"/>.
    /// </summary>
    public sealed class CommandHistory
    {
        public const string FileName = "history.txt";

        /// <summary>How many lines are kept; the oldest goes past it.</summary>
        public const int MaxEntries = 100;

        private const string LegacyPrefsKey = "DevConsole_History";

        private readonly List<string> _history = new();
        private int _cursor = -1;
        private string? _savedInput;

        /// <summary>Full path of <c>history.txt</c>.</summary>
        public static string FilePath => Path.Combine(ConsoleConfig.Directory, FileName);

        /// <summary>Read-only view of all history entries (oldest first).</summary>
        public IReadOnlyList<string> Entries => _history;

        internal CommandHistory() => Load();

        public void Add(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            if (_history.Count > 0 && _history[^1] == command) { ResetCursor(); return; }
            _history.Add(command);
            if (_history.Count > MaxEntries) _history.RemoveAt(0);
            ResetCursor();
            Save();
        }

        public string? NavigateUp(string currentInput)
        {
            if (_history.Count == 0) return null;
            if (_cursor == -1) { _savedInput = currentInput; _cursor = _history.Count - 1; }
            else if (_cursor > 0) _cursor--;
            return _history[_cursor];
        }

        public string? NavigateDown()
        {
            if (_cursor == -1) return null;
            _cursor++;
            if (_cursor >= _history.Count) { _cursor = -1; return _savedInput; }
            return _history[_cursor];
        }

        public void ResetCursor() { _cursor = -1; _savedInput = null; }

        /// <summary>Removes all history entries and the file.</summary>
        public void Clear()
        {
            _history.Clear();
            ResetCursor();
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ConsoleDiagnostics.Warning($"Failed to delete {FilePath}: {e.Message}");
            }

            DeleteLegacy();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(ConsoleConfig.Directory);
                File.WriteAllLines(FilePath, _history);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ConsoleDiagnostics.Warning($"Failed to write {FilePath}: {e.Message}");
                return;
            }

            // The file holds everything now, so the pre-4.0 copy must not come back if it is ever deleted
            DeleteLegacy();
        }

        private void Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        if (!string.IsNullOrWhiteSpace(line)) _history.Add(line);
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    ConsoleDiagnostics.Warning($"Failed to read {FilePath}: {e.Message}");
                }
            }
            else if (PlayerPrefs.HasKey(LegacyPrefsKey))
            {
                // Before 4.0 the history was JSON in PlayerPrefs; it moves into the file on the next new line
                try
                {
                    var data = JsonUtility.FromJson<LegacyData>(PlayerPrefs.GetString(LegacyPrefsKey));
                    if (data?.commands != null) _history.AddRange(data.commands);
                }
                catch (ArgumentException e)
                {
                    ConsoleDiagnostics.Warning($"Failed to read the saved command history: {e.Message}");
                }
            }

            if (_history.Count > MaxEntries) _history.RemoveRange(0, _history.Count - MaxEntries);
        }

        private static void DeleteLegacy()
        {
            if (!PlayerPrefs.HasKey(LegacyPrefsKey)) return;
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
            PlayerPrefs.Save();
        }

        [Serializable]
        private class LegacyData { public List<string> commands = new(); }
    }
}
