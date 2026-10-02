using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>Autocomplete provider that suggests quality level names from QualitySettings.</summary>
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
