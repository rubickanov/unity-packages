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
            foreach (var kvp in source)
                _collection.Add(kvp.Key, kvp.Value);
        }
    }
}
