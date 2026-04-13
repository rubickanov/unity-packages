using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Type-erased index of entities keyed by the aspect types they carry.
    /// Owned by <see cref="World"/> via composition so it stays independently testable
    /// without Unity's MonoBehaviour lifecycle.
    /// </summary>
    public sealed class EntityRegistry
    {
        private readonly Dictionary<Type, HashSet<IEntity>> _index = new();

        // By-id lookup. Populated via RegisterById at entity construction — independent of the
        // per-aspect _index so an entity is findable by id even before its first Require<T>.
        private readonly Dictionary<ulong, IEntity> _byId = new();

        /// <summary>
        /// Registers <paramref name="entity"/> in the by-id index under its own <see cref="IEntity.Id"/>.
        /// Collision semantics are strict on purpose — silent overwrite of a slot with a different
        /// entity is the kind of bug where a query "loses" a live entity and hours get burned hunting it.
        /// <list type="bullet">
        /// <item>slot empty → insert;</item>
        /// <item>slot holds the same reference → idempotent no-op;</item>
        /// <item>slot holds a different entity → <see cref="InvalidOperationException"/>.</item>
        /// </list>
        /// With <see cref="EntityId.Allocate"/> backing every real id, collisions shouldn't happen in
        /// practice — the throw path catches tests that fabricate ids manually and any future code
        /// path that tries to re-register a recycled id.
        /// </summary>
        public void RegisterById(IEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            var key = entity.Id.Value;
            if (_byId.TryGetValue(key, out var existing))
            {
                if (ReferenceEquals(existing, entity)) return;
                throw new InvalidOperationException(
                    $"EntityId {entity.Id} is already registered to a different entity. " +
                    $"This indicates an id collision — likely a manually-constructed EntityId reusing an existing value.");
            }
            _byId[key] = entity;
        }

        /// <summary>
        /// Removes <paramref name="entity"/> from the by-id index. Only clears the slot if it
        /// currently holds the same reference — a stale Unregister issued by a previous owner
        /// cannot knock a newer entity out of the index. Mirrors the defensive check in
        /// <see cref="RegisterById"/>.
        /// </summary>
        public void UnregisterById(IEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            var key = entity.Id.Value;
            if (_byId.TryGetValue(key, out var existing) && ReferenceEquals(existing, entity))
                _byId.Remove(key);
        }

        /// <summary>
        /// Looks up an entity by its <see cref="IEntity.Id"/>. Returns false immediately for
        /// <see cref="EntityId.None"/> — the id 0 slot is reserved as "no reference" and never
        /// populated. For any other id, does a single dictionary lookup.
        /// </summary>
        public bool TryFindById(EntityId id, [NotNullWhen(true)] out IEntity? entity)
        {
            if (id.IsNone)
            {
                entity = null;
                return false;
            }
            return _byId.TryGetValue(id.Value, out entity);
        }

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
            _byId.Clear();
        }
    }
}
