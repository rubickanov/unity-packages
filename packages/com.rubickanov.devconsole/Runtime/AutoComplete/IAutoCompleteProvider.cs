using System.Collections.Generic;

namespace Rubickanov.DevConsole
{
    /// <summary>Provides autocomplete suggestions for a console command argument.</summary>
    public interface IAutoCompleteProvider
    {
        /// <summary>Appends matching suggestions to <paramref name="results"/>. Must not allocate.</summary>
        void GetSuggestions(string partial, List<string> results);

        /// <summary>Short hint shown in usage string (e.g. "&lt;true|false&gt;"). Null to use parameter name.</summary>
        string Hint => null;
    }
}
