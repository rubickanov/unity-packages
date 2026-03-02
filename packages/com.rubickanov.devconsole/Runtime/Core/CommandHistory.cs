using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    public class CommandHistory
    {
        private readonly List<string> _history = new();
        private int _cursor = -1;
        private string? _savedInput;
        private const int MaxHistory = 100;
        private const string PrefsKey = "DevConsole_History";

        /// <summary>The most recently created CommandHistory instance.</summary>
        public static CommandHistory? Current { get; private set; }

        /// <summary>Read-only view of all history entries (oldest first).</summary>
        public IReadOnlyList<string> Entries => _history;

        public CommandHistory() { Load(); Current = this; }

        public void Add(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            if (_history.Count > 0 && _history[^1] == command) { ResetCursor(); return; }
            _history.Add(command);
            if (_history.Count > MaxHistory) _history.RemoveAt(0);
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

        private void Save()
        {
            var json = JsonUtility.ToJson(new HistoryData { commands = _history });
            PlayerPrefs.SetString(PrefsKey, json);
        }

        private void Load()
        {
            if (!PlayerPrefs.HasKey(PrefsKey)) return;
            try
            {
                var data = JsonUtility.FromJson<HistoryData>(PlayerPrefs.GetString(PrefsKey));
                if (data?.commands != null) _history.AddRange(data.commands);
            }
            catch (Exception e) { Debug.LogWarning($"[DevConsole] Failed to load command history: {e.Message}"); }
        }

        [Serializable]
        private class HistoryData { public List<string> commands = new(); }
    }
}
