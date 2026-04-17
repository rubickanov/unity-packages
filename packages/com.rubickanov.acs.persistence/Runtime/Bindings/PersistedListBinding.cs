using System.Collections.Generic;
using ObservableCollections;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal sealed class PersistedListBinding<T> : PersistedFieldBinding
    {
        private readonly ObservableList<T> _collection;

        public PersistedListBinding(ObservableList<T> collection)
        {
            Debug.Assert(collection != null, "PersistedListBinding: collection is null — factory must reject uninitialized [PersistedState] fields.");
            _collection = collection;
        }

        public override object ReadValue()
        {
            // Copy contents into a plain List<T> so the snapshot is detachable
            // and serializable by any downstream save layer.
            var snapshot = new List<T>(_collection.Count);
            for (int i = 0; i < _collection.Count; i++)
                snapshot.Add(_collection[i]);
            return snapshot;
        }

        public override void WriteValue(object value)
        {
            _collection.Clear();
            if (value == null) return;

            var source = (IEnumerable<T>)value;
            foreach (var item in source)
                _collection.Add(item);
        }
    }
}
