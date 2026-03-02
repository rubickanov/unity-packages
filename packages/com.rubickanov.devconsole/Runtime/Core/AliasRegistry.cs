using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>Manages command aliases. Aliases map a short name to a full command string.</summary>
    public class AliasRegistry
    {
        private static AliasRegistry? _instance;
        public static AliasRegistry Instance => _instance ??= new AliasRegistry();

        private const string PrefsKey = "DevConsole_Aliases";

        private readonly Dictionary<string, string> _aliases = new();

        /// <summary>All registered aliases.</summary>
        public IReadOnlyDictionary<string, string> Aliases => _aliases;

        private AliasRegistry() { Load(); }

        /// <summary>Returns true if the name is a registered alias and outputs the command it maps to.</summary>
        public bool TryResolve(string name, [NotNullWhen(true)] out string? command)
        {
            return _aliases.TryGetValue(name.ToLowerInvariant(), out command);
        }

        /// <summary>Creates or overwrites an alias.</summary>
        public void Set(string name, string command)
        {
            _aliases[name.ToLowerInvariant()] = command;
            Save();
        }

        /// <summary>Removes an alias. Returns true if it existed.</summary>
        public bool Remove(string name)
        {
            var removed = _aliases.Remove(name.ToLowerInvariant());
            if (removed) Save();
            return removed;
        }

        private void Save()
        {
            var data = new AliasData();
            foreach (var kvp in _aliases)
            {
                data.keys.Add(kvp.Key);
                data.values.Add(kvp.Value);
            }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        }

        private void Load()
        {
            if (!PlayerPrefs.HasKey(PrefsKey)) return;
            try
            {
                var data = JsonUtility.FromJson<AliasData>(PlayerPrefs.GetString(PrefsKey));
                if (data?.keys == null || data.values == null) return;
                for (int i = 0; i < data.keys.Count && i < data.values.Count; i++)
                    _aliases[data.keys[i]] = data.values[i];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevConsole] Failed to load aliases: {e.Message}");
            }
        }

        [Serializable]
        private class AliasData
        {
            public List<string> keys = new();
            public List<string> values = new();
        }
    }
}
