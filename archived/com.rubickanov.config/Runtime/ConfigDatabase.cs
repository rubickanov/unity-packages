using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Rubickanov.Config
{
    /// <summary>
    /// Base class for database-style configs containing collections of items.
    /// Provides O(1) Get(id) lookup and read-only access to all items.
    /// </summary>
    /// <typeparam name="TData">Data type that implements IIdentifiable</typeparam>
    public abstract class ConfigDatabase<TData> : ConfigBase
        where TData : ConfigBase, IIdentifiable
    {
        [SerializeField] private List<TData> _items = new();

        private Dictionary<string, TData>? _lookup;

        /// <summary>
        /// Get item by ID. Returns null if not found. O(1) via lazy dictionary.
        /// </summary>
        public TData? Get(string id)
        {
            // Guard null/empty up front: the lookup dictionary would otherwise throw
            // ArgumentNullException on a null key, contradicting the "returns null if not found"
            // contract. An empty/missing id is simply "not found".
            if (string.IsNullOrEmpty(id)) return null;

            if (_lookup == null)
            {
                BuildLookup();
            }

            return _lookup!.TryGetValue(id, out var value) ? value : null;
        }

        /// <summary>
        /// All items in this database.
        /// </summary>
        public IReadOnlyList<TData> All => _items;

        public override bool Validate()
        {
            List<string>? duplicates = null;
            List<int>? emptyIds = null;
            var seen = new HashSet<string>(_items.Count);

            for (int i = 0; i < _items.Count; i++)
            {
                var id = _items[i].Id;
                if (string.IsNullOrEmpty(id))
                {
                    emptyIds ??= new List<int>();
                    emptyIds.Add(i);
                    continue;
                }

                if (!seen.Add(id))
                {
                    duplicates ??= new List<string>();
                    duplicates.Add(id);
                }
            }

            if (duplicates == null && emptyIds == null)
            {
                return true;
            }

            var sb = new StringBuilder();
            sb.Append(GetType().Name).Append(": invalid items.");
            if (emptyIds != null)
            {
                sb.Append(" Empty Id at index(es): ").Append(string.Join(", ", emptyIds)).Append('.');
            }
            if (duplicates != null)
            {
                sb.Append(" Duplicate Id(s): ").Append(string.Join(", ", duplicates)).Append('.');
            }
            Debug.LogError(sb.ToString(), this);
            return false;
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            _lookup = null;
        }
#endif

        private void BuildLookup()
        {
            // Build into a local and publish to _lookup only after the duplicate check passes.
            // Assigning a partial dictionary up front means a later throw still leaves _lookup
            // non-null, so every subsequent Get would skip the rebuild and silently succeed —
            // same input throwing once then succeeding.
            var lookup = new Dictionary<string, TData>(_items.Count);
            List<string>? duplicates = null;

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var id = item.Id;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!lookup.TryAdd(id, item))
                {
                    duplicates ??= new List<string>();
                    duplicates.Add(id);
                }
            }

            if (duplicates != null)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} has duplicate Id(s): {string.Join(", ", duplicates)}");
            }

            _lookup = lookup;
        }
    }
}
