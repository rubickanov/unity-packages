using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Suggests InputSystem key names for a <see cref="KeyChord"/> argument, after any modifiers already typed:
    /// <c>ctrl+F</c> suggests <c>ctrl+F1</c>, <c>ctrl+F2</c>…
    /// </summary>
    public class KeyChordProvider : IAutoCompleteProvider
    {
        private static string[]? _keyNames;

        public string Hint => "<key>";

        public void GetSuggestions(string partial, List<string> results)
        {
            _keyNames ??= BuildKeyNames();

            var split = partial.LastIndexOf('+') + 1;
            var prefix = partial.Substring(0, split);
            var keyPartial = partial.Substring(split);

            for (int i = 0; i < _keyNames.Length; i++)
            {
                if (_keyNames[i].StartsWith(keyPartial, StringComparison.OrdinalIgnoreCase))
                    results.Add(prefix.Length == 0 ? _keyNames[i] : prefix + _keyNames[i]);
            }
        }

        private static string[] BuildKeyNames()
        {
            var names = new List<string>();
            foreach (var name in Enum.GetNames(typeof(Key)))
            {
                // None is no key, IMESelected is not a physical one
                if (name != "None" && name != "IMESelected")
                    names.Add(name);
            }
            return names.ToArray();
        }
    }
}
