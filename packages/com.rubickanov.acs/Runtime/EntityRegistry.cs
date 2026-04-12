using System;
using System.Collections.Generic;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Type-erased index of entities keyed by the aspect types they carry.
    /// Owned by <see cref="World"/> via composition so it stays independently testable
    /// without Unity's MonoBehaviour lifecycle.
    /// </summary>
    public sealed class EntityRegistry
    {
        private static readonly HashSet<EntityContext> Empty = new();

        private readonly Dictionary<Type, HashSet<EntityContext>> _index = new();

        /// <summary>
        /// Records that <paramref name="entity"/> carries an aspect of <paramref name="aspectType"/>.
        /// Idempotent: repeated calls with the same arguments are no-ops.
        /// </summary>
        public void Register(EntityContext entity, Type aspectType)
        {
            if (!_index.TryGetValue(aspectType, out var set))
            {
                set = new HashSet<EntityContext>();
                _index[aspectType] = set;
            }
            set.Add(entity);
        }

        /// <summary>
        /// Removes <paramref name="entity"/> from every aspect bucket.
        /// Safe to call for entities that were never registered.
        /// </summary>
        public void Unregister(EntityContext entity)
        {
            foreach (var set in _index.Values)
                set.Remove(entity);
        }

        /// <summary>
        /// Returns the set of entities carrying <paramref name="aspectType"/>. The returned
        /// collection is owned by the registry — do not mutate it, and treat it as invalidated
        /// after the next Register/Unregister call.
        /// </summary>
        public IReadOnlyCollection<EntityContext> GetAllWith(Type aspectType)
        {
            return _index.TryGetValue(aspectType, out var set) ? set : Empty;
        }

        /// <summary>
        /// Drops every tracked entity. Useful for tests and for resetting the registry
        /// when a new <see cref="World"/> takes over.
        /// </summary>
        public void Clear()
        {
            _index.Clear();
        }
    }
}
