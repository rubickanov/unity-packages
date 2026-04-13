using System;
using System.Collections.Generic;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Type-erased index of entities keyed by the aspect types they carry.
    /// Owned by <see cref="WorldCore"/> via composition so it stays independently testable
    /// without Unity's MonoBehaviour lifecycle.
    /// </summary>
    public sealed class EntityRegistry
    {
        private readonly Dictionary<Type, HashSet<IEntity>> _index = new();

        /// <summary>
        /// Records that <paramref name="entity"/> carries an aspect of <paramref name="aspectType"/>.
        /// Idempotent: repeated calls with the same arguments are no-ops.
        /// </summary>
        public void Register(IEntity entity, Type aspectType)
        {
            if (!_index.TryGetValue(aspectType, out var set))
            {
                set = new HashSet<IEntity>();
                _index[aspectType] = set;
            }
            set.Add(entity);
        }

        /// <summary>
        /// Removes <paramref name="entity"/> from each bucket listed in <paramref name="aspectTypes"/>.
        /// Pass the entity's own aspect-type set (e.g. <see cref="IEntity.AspectTypes"/>) so the
        /// registry touches only the buckets the entity actually belongs to — O(k) instead of
        /// O(types-in-world). Safe for aspect types that were never registered.
        /// </summary>
        /// <remarks>
        /// The parameter is the concrete <see cref="Dictionary{TKey,TValue}.KeyCollection"/> rather
        /// than <see cref="IEnumerable{T}"/> so <c>foreach</c> duck-types to the struct enumerator
        /// and the despawn hot path allocates nothing.
        /// </remarks>
        public void Unregister(IEntity entity, Dictionary<Type, object>.KeyCollection aspectTypes)
        {
            foreach (var t in aspectTypes)
                if (_index.TryGetValue(t, out var set))
                    set.Remove(entity);
        }

        /// <summary>
        /// Returns the set of entities carrying <paramref name="aspectType"/>. The returned
        /// collection is owned by the registry — do not mutate it, and treat it as invalidated
        /// after the next Register/Unregister call.
        /// </summary>
        public IReadOnlyCollection<IEntity> GetAllWith(Type aspectType)
        {
            return _index.TryGetValue(aspectType, out var set) ? set : Array.Empty<IEntity>();
        }

        /// <summary>
        /// Returns the raw bucket for <paramref name="aspectType"/>, or null if no entity
        /// carries it. Intended for zero-alloc iteration via <see cref="EntityQuery{T}"/>:
        /// callers get direct access to the <see cref="HashSet{T}.Enumerator"/> value-struct
        /// without the boxing that <see cref="GetAllWith"/>'s <see cref="IReadOnlyCollection{T}"/>
        /// return type would force. Internal by design — must not be mutated externally.
        /// </summary>
        internal HashSet<IEntity>? GetBucketOrNull(Type aspectType)
        {
            return _index.TryGetValue(aspectType, out var set) ? set : null;
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
