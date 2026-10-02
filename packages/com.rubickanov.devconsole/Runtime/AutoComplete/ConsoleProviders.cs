using System;
using System.Collections.Generic;

namespace Rubickanov.DevConsole
{
    /// <summary>Suggests the names of existing aliases.</summary>
    public sealed class AliasNameProvider : IAutoCompleteProvider
    {
        private readonly CommandRegistry _registry;

        public AliasNameProvider(CommandRegistry registry) => _registry = registry;

        public string Hint => "<alias>";

        public void GetSuggestions(string partial, List<string> results)
        {
            var names = _registry.Aliases.SortedNames;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(names[i]);
            }
        }
    }

    /// <summary>Suggests the keys that have a binding.</summary>
    public sealed class BoundKeyProvider : IAutoCompleteProvider
    {
        private readonly CommandRegistry _registry;

        public BoundKeyProvider(CommandRegistry registry) => _registry = registry;

        public string Hint => "<key>";

        public void GetSuggestions(string partial, List<string> results)
        {
            foreach (var chord in _registry.Bindings.Bindings.Keys)
            {
                var name = chord.ToString();
                if (name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(name);
            }
        }
    }
}
