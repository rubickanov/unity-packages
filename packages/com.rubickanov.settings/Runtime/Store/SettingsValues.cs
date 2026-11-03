using System.Collections.Generic;

namespace Rubickanov.Settings
{
    /// <summary>A read file by dotted id, in the file's order: what the settings take, and what nobody declared.</summary>
    public sealed class SettingsValues
    {
        private readonly List<string> _ids = new();
        private readonly Dictionary<string, SettingsJsonValue> _values = new();

        public IEnumerable<string> Ids => _ids;

        public int Count => _ids.Count;

        public bool Has(string id) => _values.ContainsKey(id);

        public bool TryGet(string id, out SettingsJsonValue value) => _values.TryGetValue(id, out value);

        public void Set(string id, SettingsJsonValue value)
        {
            if (!_values.ContainsKey(id)) _ids.Add(id);
            _values[id] = value;
        }

        /// <summary>Moves a value to a new id in its place; false when there is none or the new id is taken.</summary>
        public bool Rename(string from, string to)
        {
            if (!_values.TryGetValue(from, out SettingsJsonValue value) || _values.ContainsKey(to)) return false;
            _ids[_ids.IndexOf(from)] = to;
            _values.Remove(from);
            _values[to] = value;
            return true;
        }

        public bool Remove(string id)
        {
            if (!_values.Remove(id)) return false;
            _ids.Remove(id);
            return true;
        }

        internal string IdAt(int index) => _ids[index];

        internal void Clear()
        {
            _ids.Clear();
            _values.Clear();
        }
    }
}
