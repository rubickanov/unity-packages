using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole
{
    /// <summary>Binds keyboard keys to console commands. Executes bound commands on key press.</summary>
    public class CommandBindings : MonoBehaviour
    {
        private static CommandBindings? _instance;

        private readonly Dictionary<Key, string> _bindings = new();
        private readonly List<string> _pendingExecute = new();
        private const string PrefsKey = "DevConsole_Bindings";

        /// <summary>All registered bindings.</summary>
        public IReadOnlyDictionary<Key, string> Bindings => _bindings;

        /// <summary>Ensures the singleton MonoBehaviour exists. Creates one if needed.</summary>
        public static CommandBindings EnsureExists()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[DevConsole] CommandBindings");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CommandBindings>();
            return _instance;
        }

        /// <summary>Returns the instance if it exists, null otherwise.</summary>
        public static CommandBindings? GetInstance() => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            Load();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (_bindings.Count == 0) return;
            if (Keyboard.current == null) return;

            // Don't execute binds while console is open
            if (DevConsoleIMGUI.IsOpen) return;
            if (DevConsoleUIToolkit.Instance != null && DevConsoleUIToolkit.Instance.IsVisible) return;

            // Collect first, then execute. A bound command may be `bind`/`unbind`, which mutates
            // _bindings — executing inside the foreach would throw "Collection was modified".
            _pendingExecute.Clear();
            foreach (var kvp in _bindings)
            {
                if (Keyboard.current[kvp.Key].wasPressedThisFrame)
                    _pendingExecute.Add(kvp.Value);
            }

            for (int i = 0; i < _pendingExecute.Count; i++)
                CommandRegistry.Instance.Execute(_pendingExecute[i]);
        }

        /// <summary>Binds a key to a command string.</summary>
        public void Bind(Key key, string command)
        {
            _bindings[key] = command;
            Save();
        }

        /// <summary>Removes a key binding. Returns true if it existed.</summary>
        public bool Unbind(Key key)
        {
            var removed = _bindings.Remove(key);
            if (removed) Save();
            return removed;
        }

        /// <summary>Removes all bindings and clears persisted storage.</summary>
        public void Clear()
        {
            _bindings.Clear();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        /// <summary>Parses a key name string to a Key enum value.</summary>
        public static bool TryParseKey(string keyName, out Key key)
        {
            return Enum.TryParse(keyName, true, out key) && key != Key.None;
        }

        private void Save()
        {
            var data = new BindingsData();
            foreach (var kvp in _bindings)
            {
                data.keys.Add(kvp.Key.ToString());
                data.values.Add(kvp.Value);
            }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        }

        private void Load()
        {
            if (!PlayerPrefs.HasKey(PrefsKey)) return;
            try
            {
                var data = JsonUtility.FromJson<BindingsData>(PlayerPrefs.GetString(PrefsKey));
                if (data?.keys == null || data.values == null) return;
                for (int i = 0; i < data.keys.Count && i < data.values.Count; i++)
                {
                    if (Enum.TryParse<Key>(data.keys[i], true, out var key))
                        _bindings[key] = data.values[i];
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevConsole] Failed to load bindings: {e.Message}");
            }
        }

        [Serializable]
        private class BindingsData
        {
            public List<string> keys = new();
            public List<string> values = new();
        }
    }
}
