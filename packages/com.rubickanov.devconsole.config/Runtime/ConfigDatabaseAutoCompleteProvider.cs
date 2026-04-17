using System;
using System.Collections.Generic;
using Rubickanov.Config;

namespace Rubickanov.DevConsole.Config
{
    /// <summary>
    /// Autocomplete provider that suggests <see cref="IIdentifiable.Id"/> values from a <see cref="ConfigDatabase{T}"/>.
    /// </summary>
    public sealed class ConfigDatabaseAutoCompleteProvider<T> : IAutoCompleteProvider
        where T : ConfigBase, IIdentifiable
    {
        private readonly ConfigDatabase<T> _db;
        private readonly string _hint;

        public ConfigDatabaseAutoCompleteProvider(ConfigDatabase<T> db, string? hint = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            _db = db;
            _hint = hint ?? $"<{typeof(T).Name}>";
        }

        public string Hint => _hint;

        public void GetSuggestions(string partial, List<string> results)
        {
            var all = _db.All;
            for (int i = 0; i < all.Count; i++)
            {
                var id = all[i].Id;
                if (string.IsNullOrEmpty(id)) continue;

                if (string.IsNullOrEmpty(partial) ||
                    id.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(id);
            }
        }
    }
}
