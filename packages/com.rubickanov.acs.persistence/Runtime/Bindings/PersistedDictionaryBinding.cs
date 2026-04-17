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
            _collection.Clear();
            if (value == null) return;

            var source = (IEnumerable<KeyValuePair<TKey, TValue>>)value;
            foreach (var kvp in source)
                _collection.Add(kvp.Key, kvp.Value);
        }
    }
}
