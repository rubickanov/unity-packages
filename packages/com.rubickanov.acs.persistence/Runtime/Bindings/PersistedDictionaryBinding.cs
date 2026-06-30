using System.Collections.Generic;
using ObservableCollections;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal sealed class PersistedDictionaryBinding<TKey, TValue> : PersistedFieldBinding
    {
        private readonly ObservableDictionary<TKey, TValue> _collection;

        public PersistedDictionaryBinding(ObservableDictionary<TKey, TValue> collection)
        {
            Debug.Assert(collection != null, "PersistedDictionaryBinding: collection is null — factory must reject uninitialized [PersistedState] fields.");
            _collection = collection;
        }

        public override object ReadValue()
        {
            var snapshot = new Dictionary<TKey, TValue>(_collection.Count);
            foreach (var kvp in _collection)
                snapshot.Add(kvp.Key, kvp.Value);
            return snapshot;
        }

        public override void WriteValue(object value)
        {
            if (value == null)
            {
                _collection.Clear();
                return;
            }

            // Cast BEFORE clearing: a type-mismatched snapshot (e.g. Dictionary<string,long>
            // for an ObservableDictionary<string,int>) throws InvalidCastException here, and the
            // restore loop's catch keeps the live collection intact instead of leaving it wiped.
            var source = (IEnumerable<KeyValuePair<TKey, TValue>>)value;

            _collection.Clear();
            // Upsert via indexer rather than Add: a duplicate-key source (the permissive cast
            // intentionally accepts list-of-pairs shapes) would throw ArgumentException on the
            // second Add — and that escapes the per-field restore catch (InvalidCast/NRE only),
            // aborting the whole entity *and* world restore with the dict left half-populated.
            // Last value wins, mirroring how the source would have been read back.
            foreach (var kvp in source)
                _collection[kvp.Key] = kvp.Value;
        }
    }
}
