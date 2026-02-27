using System;
using System.Collections.Generic;

namespace Rubickanov.DevConsole
{
    /// <summary>Autocomplete provider that suggests enum value names. Auto-applied to enum parameters.</summary>
    public class EnumAutoCompleteProvider : IAutoCompleteProvider
    {
        private readonly string[] _values;
        private readonly string _enumName;

        public string Hint => $"<{_enumName}>";

        public EnumAutoCompleteProvider(Type enumType)
        {
            _enumName = enumType.Name;
            _values = Enum.GetNames(enumType);
        }

        public void GetSuggestions(string partial, List<string> results)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                if (string.IsNullOrEmpty(partial) ||
                    _values[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(_values[i]);
            }
        }
    }

    /// <summary>Autocomplete provider that suggests from a fixed list of strings.</summary>
    public class StaticListProvider : IAutoCompleteProvider
    {
        private readonly string[] _options;
        public string Hint => "<option>";

        public StaticListProvider(params string[] options) => _options = options;

        public void GetSuggestions(string partial, List<string> results)
        {
            for (int i = 0; i < _options.Length; i++)
            {
                if (string.IsNullOrEmpty(partial) ||
                    _options[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(_options[i]);
            }
        }
    }

    /// <summary>Autocomplete provider for boolean parameters. Auto-applied to bool parameters.</summary>
    public class BoolAutoCompleteProvider : IAutoCompleteProvider
    {
        public static readonly BoolAutoCompleteProvider Instance = new();
        private static readonly string[] Values = { "true", "false" };
        public string Hint => "<true|false>";

        public void GetSuggestions(string partial, List<string> results)
        {
            for (int i = 0; i < Values.Length; i++)
            {
                if (string.IsNullOrEmpty(partial) ||
                    Values[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(Values[i]);
            }
        }
    }
}
