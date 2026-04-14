using System.Collections.Generic;
using ObservableCollections;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal sealed class PersistedHashSetBinding<T> : PersistedFieldBinding
    {
        private readonly ObservableHashSet<T> _collection;

        public PersistedHashSetBinding(ObservableHashSet<T> collection)
        {
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
            _collection.Clear();
            if (value == null) return;

            var source = (IEnumerable<T>)value;
            foreach (var item in source)
                _collection.Add(item);
        }
    }
}
