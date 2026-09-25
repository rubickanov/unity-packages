using System;
using System.Collections.Generic;

namespace Rubickanov.DevConsole
{
    /// <summary>Suggests the names of existing aliases.</summary>
    public class AliasNameProvider : IAutoCompleteProvider
    {
        public static readonly AliasNameProvider Instance = new();
        public string Hint => "<alias>";

        public void GetSuggestions(string partial, List<string> results)
        {
            var names = AliasRegistry.Instance.SortedNames;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(names[i]);
            }
        }
    }

    /// <summary>Suggests the keys that have a binding.</summary>
    public class BoundKeyProvider : IAutoCompleteProvider
    {
        public static readonly BoundKeyProvider Instance = new();
        public string Hint => "<key>";

        public void GetSuggestions(string partial, List<string> results)
        {
            foreach (var chord in BindingRegistry.Instance.Bindings.Keys)
            {
                var name = chord.ToString();
                if (name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(name);
            }
        }
    }
}
