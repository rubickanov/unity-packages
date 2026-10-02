using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.DevConsole
{
    /// <summary>Autocomplete provider that suggests quality level names from QualitySettings.</summary>
    [Preserve]
    public class QualityLevelProvider : IAutoCompleteProvider
    {
        public string Hint => "<quality>";

        public void GetSuggestions(string partial, List<string> results)
        {
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(partial) ||
                    names[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(names[i]);
            }
        }
    }
}
