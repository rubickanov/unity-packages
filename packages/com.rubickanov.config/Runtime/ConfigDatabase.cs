using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rubickanov.Config
{
    /// <summary>
    /// Base class for database-style configs containing collections of items.
    /// Provides O(1) Get(id) lookup and read-only access to all items.
    /// </summary>
    /// <typeparam name="TData">Data type that implements IIdentifiable</typeparam>
    public abstract class ConfigDatabase<TData> : ConfigBase
        where TData : class, IIdentifiable
    {
        [SerializeField] private List<TData> _items = new();

        private Dictionary<string, TData>? _lookup;

        /// <summary>
        /// Get item by ID. Returns null if not found. O(1) via lazy dictionary.
        /// </summary>
        public TData? Get(string id)
        {
            _lookup ??= _items.ToDictionary(i => i.Id);
            return _lookup.TryGetValue(id, out var item) ? item : null;
        }

        /// <summary>
        /// All items in this database.
        /// </summary>
        public IReadOnlyList<TData> All => _items;
    }
}
