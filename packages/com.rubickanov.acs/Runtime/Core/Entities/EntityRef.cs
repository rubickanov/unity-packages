using System;
using System.Diagnostics.CodeAnalysis;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Domain-level reference from one entity to another. A value-type wrapper around
    /// <see cref="EntityId"/> that carries the intent "this field points at another entity"
    /// and exposes the resolve path (<see cref="TryResolve"/>) so call sites don't hand-roll
    /// <c>world.TryFindById(storedId, out …)</c> every time.
    /// <para/>
    /// Typical use — aspect data that references another entity (e.g. a zombie holding a
    /// player as its target):
    /// <code>
    /// public readonly ReactiveProperty&lt;EntityRef&gt; Target = new(EntityRef.None);
    /// // ...
    /// if (aspect.Target.Value.TryResolve(world, out var target)) { /* use target */ }
    /// </code>
    /// Use <see cref="EntityId"/> directly for infrastructure — registry keys, save payloads,
    /// network messages, transport DTOs — and reach for <see cref="EntityRef"/> only when the
    /// field semantically is a reference to an entity in gameplay state.
    /// <para/>
    /// Intentionally an unmanaged struct (only field is <see cref="EntityId"/>, which is only
    /// a <see cref="ulong"/>) so it flows through acs.netcode's <c>[Replicated]</c> path
    /// unchanged — the replication scanner validates the inner type is unmanaged, and this
    /// wrapper preserves that property. No cached <see cref="IEntity"/> reference on purpose:
    /// (1) a managed field would break replication, (2) a cache would go stale once the
    /// pointed-at entity is destroyed (use-after-free in logic). Resolve each time; the
    /// by-id registry is an O(1) dictionary lookup.
    /// </summary>
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        /// <summary>
        /// The wrapped id. Exposed so serializers and tooling can get at the raw handle without
        /// going through <see cref="TryResolve"/>.
        /// </summary>
        public readonly EntityId Id;

        /// <summary>
        /// Wraps an explicit id. Normal gameplay code should prefer <see cref="From"/> which
        /// reads the id off an entity — this ctor exists for deserialization and for call sites
        /// that have already obtained an <see cref="EntityId"/>.
        /// </summary>
        public EntityRef(EntityId id)
        {
            Id = id;
        }

        /// <summary>
        /// Builds a ref from an entity. Null-safe: <c>From(null)</c> returns <see cref="None"/>
        /// so callers can pipe through an <see cref="IEntity"/> that may not exist yet without
        /// hand-written null checks.
        /// </summary>
        public static EntityRef From(IEntity? entity)
            => entity is null ? None : new EntityRef(entity.Id);

        /// <summary>
        /// The "no reference" value — equivalent to <c>default(EntityRef)</c>. Use as the initial
        /// value of target-style fields. <see cref="TryResolve"/> on <see cref="None"/> returns
        /// false without touching the world.
        /// </summary>
        public static EntityRef None => default;

        /// <summary>True iff this ref points at no entity (wraps <see cref="EntityId.None"/>).</summary>
        public bool IsNone => Id.IsNone;

        /// <summary>
        /// Resolves the referenced entity through <paramref name="world"/>. Returns false — and
        /// <paramref name="entity"/> is null — for <see cref="None"/>, for ids that belong to a
        /// different world, and for entities that have been destroyed. The ref is intentionally
        /// allowed to outlive the entity it points to (dangling ref); callers check this result
        /// instead of getting a stale cached reference.
        /// </summary>
        public bool TryResolve(World world, [NotNullWhen(true)] out IEntity? entity)
        {
            if (IsNone)
            {
                entity = null;
                return false;
            }

            return world.TryFindById(Id, out entity);
        }

        /// <summary>
        /// Shortcut for the common case where the caller only wants the entity (or null if the
        /// ref is <see cref="None"/> or dangling). Equivalent to
        /// <c>TryResolve(world, out var e) ? e : null</c>.
        /// </summary>
        public IEntity? ResolveOrNull(World world)
            => TryResolve(world, out var entity) ? entity : null;

        /// <summary>
        /// True iff the ref currently points at a live entity in <paramref name="world"/>.
        /// Cheap check for AI / targeting code that wants to drop stale references without
        /// actually needing the <see cref="IEntity"/> instance.
        /// </summary>
        public bool IsAlive(World world) => TryResolve(world, out _);

        public bool Equals(EntityRef other) => Id.Equals(other.Id);

        public override bool Equals(object? obj) => obj is EntityRef other && Equals(other);

        public override int GetHashCode() => Id.GetHashCode();

        public static bool operator ==(EntityRef left, EntityRef right) => left.Id == right.Id;

        public static bool operator !=(EntityRef left, EntityRef right) => left.Id != right.Id;

        public override string ToString() => IsNone ? "EntityRef#None" : $"EntityRef({Id})";
    }
}
