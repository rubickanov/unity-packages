using System.Collections.Generic;
using ObservableCollections;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal sealed class PersistedHashSetBinding<T> : PersistedFieldBinding
    {
        private readonly ObservableHashSet<T> _collection;

        public PersistedHashSetBinding(ObservableHashSet<T> collection)
        {
            Debug.Assert(collection != null, "PersistedHashSetBinding: collection is null — factory must reject uninitialized [PersistedState] fields.");
            _collection = collection;
        }

        public override object ReadValue()
        {
            var snapshot = new HashSet<T>();
            foreach (var item in _collection)
                snapshot.Add(item);
            return snapshot;
        }

        public override void WriteValue(object value)
        {
            if (value == null)
            {
                _collection.Clear();
                return;
            }

            // Cast BEFORE clearing: a type-mismatched snapshot (e.g. HashSet<long> for an
            // ObservableHashSet<int>) throws InvalidCastException here, and the restore loop's
            // catch keeps the live collection intact instead of leaving it wiped.
            var source = (IEnumerable<T>)value;

            _collection.Clear();
            foreach (var item in source)
                _collection.Add(item);
        }
    }
}
