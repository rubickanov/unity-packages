using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Pure-C# world context. Implements <see cref="IEntity"/> so world-scoped aspects live
    /// on the <c>World</c> itself (accessed via <see cref="Require{T}"/>), and owns the
    /// <see cref="EntityRegistry"/> so <see cref="Query{T}"/> can locate every other
    /// <see cref="IEntity"/> by aspect type. Has no Unity dependencies — construct directly
    /// for headless simulations, pocket containers, and edit-mode tests.
    /// <para/>
    /// For scene-driven Unity games, drop a <c>MonoWorld</c> component on a GameObject;
    /// it creates a <c>World</c> internally, assigns <see cref="Current"/>, and delegates
    /// its own IEntity surface into the embedded world.
    /// </summary>
    /// <remarks>
    /// The static <see cref="Current"/> slot is the "currently active world" that backs
    /// <see cref="Require{T}()"/> and <see cref="Query{T}()"/>. It is assigned via
    /// <see cref="SetCurrent"/> (by <c>MonoWorld</c> or headless callers) and cleared via
    /// <see cref="ClearCurrent"/>. Only one world may be active at a time.
    /// <para/>
    /// <b>Thread safety:</b> not thread-safe. The embedded <see cref="AspectStore"/> and
    /// <see cref="EntityRegistry"/> both perform unsynchronized dictionary operations;
    /// concurrent <c>Require</c>/<c>Register</c>/<c>Query</c> from multiple threads can
    /// corrupt the registry or produce duplicate/dropped aspects. The static
    /// <see cref="Current"/> slot is likewise an unprotected field. Headless consumers
    /// that wish to tick the world from a non-main thread must serialize access
    /// externally — a single "world lock" covering the tick boundary is the intended
    /// pattern. A locking variant may be added if a real consumer requires it; the
    /// default stays lock-free so per-call overhead matches the Unity single-thread case.
    /// </remarks>
    public sealed class World : IEntity, IDisposable
    {
        /// <summary>The currently active world, or <c>null</c> if none is assigned.</summary>
        public static World? Current { get; private set; }

        /// <summary>
        /// Fires when <see cref="Current"/> transitions from null to a newly-assigned world —
        /// i.e. on the <see cref="SetCurrent"/> call that actually changes the slot. Used by
        /// <see cref="MonoEntity"/> to retroactively register itself when it <c>Awoke</c>
        /// before any world was current (scene spawned without a <c>MonoWorld</c>, then a
        /// <c>MonoWorld</c> is dropped in later). Does not fire for <see cref="ClearCurrent"/>
        /// nor for an idempotent <c>SetCurrent</c> with the same instance.
        /// <para/>
        /// Cleared each play session via <see cref="ResetStaticEvents"/>, so Domain-Reload-disabled
        /// runs cannot carry stale subscribers from the previous session into the next.
        /// </summary>
        public static event Action<World>? CurrentChanged;

        /// <summary>
        /// Assigns <paramref name="world"/> as the <see cref="Current"/> world. Throws if a
        /// different world is already current — callers must <see cref="ClearCurrent"/> the
        /// previous one first. Idempotent for the same instance.
        /// </summary>
        public static void SetCurrent(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (Current != null && Current != world)
                throw new InvalidOperationException(
                    "Another World is already set as Current. ClearCurrent the previous world before assigning a new one.");
            var changed = Current != world;
            Current = world;
            // Fire after the slot is updated so handlers observing Current see the new value.
            // Skip the idempotent-reassign case so retroactive-register handlers don't re-run.
            if (changed) CurrentChanged?.Invoke(world);
        }

        /// <summary>
        /// Clears <see cref="Current"/> iff it points at <paramref name="world"/>. No-op if a
        /// different world is current — guards against a stale <c>ClearCurrent</c> from a
        /// previous owner overwriting a newer assignment.
        /// </summary>
        public static void ClearCurrent(World world)
        {
            if (Current == world) Current = null;
        }

        /// <summary>
        /// Hard-resets <see cref="Current"/> to null. Intended for
        /// <c>[RuntimeInitializeOnLoadMethod]</c> in <c>MonoWorld</c> so a stale static
        /// reference from the previous Play session cannot survive into the next one when
        /// Domain Reload is disabled. Not part of the public API — do not use for regular
        /// lifecycle handoff (use <see cref="ClearCurrent"/> instead).
        /// </summary>
        internal static void ForceResetCurrent() => Current = null;

        /// <summary>
        /// Clears <see cref="CurrentChanged"/> subscribers. Paired with <see cref="ForceResetCurrent"/>
        /// in <c>MonoWorld</c>'s <c>[RuntimeInitializeOnLoadMethod]</c> so Domain-Reload-disabled
        /// runs start every Play session with an empty subscriber list — otherwise handlers from
        /// destroyed MonoEntities in the previous session would fire when the new session's first
        /// MonoWorld assigns Current.
        /// </summary>
        internal static void ResetStaticEvents() => CurrentChanged = null;

        /// <summary>
        /// Shorthand for <c>Current.Require&lt;T&gt;()</c> — fetches or creates a world-scoped
        /// aspect on the active world. Throws <see cref="InvalidOperationException"/> if no
        /// <see cref="Current"/> world is assigned, matching the contract of <see cref="Query{T}"/>:
        /// if your code calls <c>World.Require</c>, the world must exist.
        /// </summary>
        public static T Require<T>() where T : class, IEntityAspect, new()
            => GetCurrentOrThrow(nameof(Require)).RequireAspectInternal<T>();

        /// <summary>
        /// Enumerates every aspect of type <typeparamref name="T"/> currently registered with
        /// <see cref="Current"/>. Throws <see cref="InvalidOperationException"/> if no world is
        /// assigned — matches <see cref="Require{T}"/> so callers cannot silently iterate an
        /// empty set when setup is missing.
        /// <para/>
        /// <b>The world itself can appear in results.</b> <see cref="World"/> implements
        /// <see cref="IEntity"/> and self-registers in both the by-id index and any per-aspect
        /// bucket it touches via <see cref="Require{T}"/>. A query for a world-scoped aspect will
        /// therefore yield the world as one of its entities. Filter callers that expect only
        /// "things in the scene" should gate with <c>if (entity is MonoEntity)</c> or keep
        /// world-scoped aspects in a separate aspect type from entity-scoped ones.
        /// </summary>
        public static EntityQuery<T> Query<T>() where T : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T>();

        /// <summary>Enumerates every entity that carries both aspect types. Throws if no Current world is assigned.</summary>
        public static EntityQuery<T1, T2> Query<T1, T2>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T1, T2>();

        /// <summary>Enumerates every entity that carries all three aspect types. Throws if no Current world is assigned.</summary>
        public static EntityQuery<T1, T2, T3> Query<T1, T2, T3>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T1, T2, T3>();

        /// <summary>Enumerates every entity that carries all four aspect types. Throws if no Current world is assigned.</summary>
        public static EntityQuery<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T1, T2, T3, T4>();

        /// <summary>Enumerates every entity that carries all five aspect types. Throws if no Current world is assigned.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5> Query<T1, T2, T3, T4, T5>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T1, T2, T3, T4, T5>();

        /// <summary>Enumerates every entity that carries all six aspect types. Throws if no Current world is assigned.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6> Query<T1, T2, T3, T4, T5, T6>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T1, T2, T3, T4, T5, T6>();

        /// <summary>Enumerates every entity that carries all seven aspect types. Throws if no Current world is assigned.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6, T7> Query<T1, T2, T3, T4, T5, T6, T7>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T1, T2, T3, T4, T5, T6, T7>();

        /// <summary>
        /// Enumerates every entity that carries all eight aspect types. Eight is the maximum
        /// overload — compose an aggregate aspect if you need more. Throws if no Current world is assigned.
        /// </summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6, T7, T8> Query<T1, T2, T3, T4, T5, T6, T7, T8>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            where T8 : class, IEntityAspect
            => GetCurrentOrThrow(nameof(Query)).QueryLocal<T1, T2, T3, T4, T5, T6, T7, T8>();

        private static World GetCurrentOrThrow(string member)
        {
            var current = Current;
            if (current == null)
                throw new InvalidOperationException(
                    $"{nameof(World)}.{member}() called but no active World is set. " +
                    $"Assign one via {nameof(SetCurrent)} (or drop a MonoWorld in the scene).");
            return current;
        }

        private readonly AspectStore _store = new();
        private readonly EntityRegistry _registry = new();
        private bool _disposed;

        /// <inheritdoc/>
        public EntityId Id { get; } = EntityId.Allocate();

        /// <summary>
        /// Registers the world as its own by-id index entry so <see cref="TryFindById"/> resolves
        /// <see cref="Id"/> to the world itself. Without self-registration <c>World</c> would
        /// formally satisfy <see cref="IEntity"/> but lookup by its own id would silently return
        /// false — an invariant leak easy to hit and hard to diagnose.
        /// </summary>
        public World()
        {
            _registry.RegisterById(this);
        }

        /// <inheritdoc/>
        public event Action<IEntity>? Destroyed;

        /// <summary>
        /// Fired every time <see cref="Require{T}"/> creates a new aspect on this world.
        /// Does not fire when <c>Require</c> returns an already-existing aspect. Used by
        /// <c>MonoWorld</c> to forward into <c>MonoEntity.OnAspectCreated</c> so world-scoped
        /// aspects participate in the same "new aspect" contract as entity-scoped ones.
        /// </summary>
        public event Action<IEntity, Type>? AspectCreated;

        /// <summary>Exposed for tests and tooling. Do not mutate directly.</summary>
        public EntityRegistry Registry => _registry;

        /// <inheritdoc/>
        // Explicit interface implementation: keeps the IEntity contract intact (callers that
        // hold a pure <see cref="World"/> via the <see cref="IEntity"/> interface still call
        // <c>Require&lt;T&gt;</c>) while freeing the public method-name <c>Require</c> on the
        // concrete <see cref="World"/> type for the <see cref="Require{T}()"/> static shortcut
        // — the two would otherwise collide (CS0111).
        T IEntity.Require<T>() => RequireAspectInternal<T>();

        private T RequireAspectInternal<T>() where T : class, IEntityAspect, new()
        {
            // Guard before the store mutation — if we only relied on Register to throw, the
            // aspect would already be in _store before the throw, leaving the disposed world
            // with a partially-created state (store has it, registry doesn't).
            if (_disposed) throw new ObjectDisposedException(nameof(World));
            var instance = _store.GetOrAdd<T>(out var created);
            // Route through the public Register(IEntity, Type) entry point so world-scoped
            // aspect creation flows through the same AspectCreated fire as entity-scoped —
            // single source of truth, no duplicated raise path.
            if (created) Register(this, typeof(T));
            return instance;
        }

        /// <inheritdoc/>
        public bool TryGet<T>([NotNullWhen(returnValue: true)] out T? aspect) where T : class, IEntityAspect
            => _store.TryGet(out aspect);

        /// <inheritdoc/>
        public bool Has<T>() where T : class, IEntityAspect => _store.Has<T>();

        /// <inheritdoc/>
        public IEnumerable<object> GetAllAspects() => _store.GetAllAspects();

        /// <inheritdoc/>
        public Dictionary<Type, object>.KeyCollection AspectTypes => _store.AspectTypes;

        /// <summary>
        /// Records that <paramref name="entity"/> carries an aspect of type
        /// <paramref name="aspectType"/>. Called by <see cref="MonoEntity.Require{T}"/>
        /// and by pure-C# <see cref="Entity.Require{T}"/> when an <c>Entity(World)</c>
        /// was constructed. Fires <see cref="AspectCreated"/> after registration so a
        /// single raise site covers both MonoEntity and pure-Entity aspect creation —
        /// subscribers (including <c>MonoWorld</c>'s forwarder into
        /// <see cref="MonoEntity.OnAspectCreated"/>) see every new aspect regardless of
        /// which entity flavor produced it.
        /// </summary>
        public void Register(IEntity entity, Type aspectType)
        {
            // A disposed world has had its registry cleared — silently accepting new
            // registrations would create orphans in the per-aspect index that nothing ever
            // cleans up, and AspectCreated subscribers would see events from a dead world.
            // Throw loudly instead so "entity outlives its world" bugs surface at the exact
            // call site. Unregister stays lenient because teardown must be robust.
            if (_disposed) throw new ObjectDisposedException(nameof(World));
            _registry.Register(entity, aspectType);
            AspectCreated?.Invoke(entity, aspectType);
        }

        /// <summary>
        /// Drops <paramref name="entity"/> from each bucket listed in <paramref name="aspectTypes"/>.
        /// Pass <see cref="IEntity.AspectTypes"/> so only the buckets the entity actually
        /// belongs to are touched. Safe for aspect types that were never registered.
        /// </summary>
        public void Unregister(IEntity entity, Dictionary<Type, object>.KeyCollection aspectTypes)
            => _registry.Unregister(entity, aspectTypes);

        /// <summary>
        /// Registers <paramref name="entity"/> in the by-id index so it becomes findable via
        /// <see cref="TryFindById"/>. Called by <see cref="Entity(World)"/> and by
        /// <see cref="MonoEntity"/>.<c>Awake</c> immediately after the id is allocated — deliberately
        /// independent of the per-aspect registration so an entity is addressable even before
        /// its first <see cref="IEntity.Require{T}"/>. Collision semantics live in
        /// <see cref="EntityRegistry.RegisterById"/>.
        /// </summary>
        public void Register(IEntity entity)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(World));
            _registry.RegisterById(entity);
        }

        /// <summary>
        /// Removes <paramref name="entity"/> from the by-id index. Called last during an entity's
        /// teardown — after per-aspect Unregister — so <see cref="Destroyed"/> subscribers can
        /// still resolve the entity via <see cref="TryFindById"/>. Safe if the entity was never
        /// registered (no-op) or if a different entity now owns the slot (leaves it intact).
        /// </summary>
        public void Unregister(IEntity entity)
            => _registry.UnregisterById(entity);

        /// <summary>
        /// Looks up an entity by its <see cref="IEntity.Id"/>. Returns false for
        /// <see cref="EntityId.None"/>, for ids belonging to a different world, and for ids of
        /// entities that have been destroyed. O(1) dictionary lookup.
        /// </summary>
        public bool TryFindById(EntityId id, [NotNullWhen(true)] out IEntity? entity)
            => _registry.TryFindById(id, out entity);

        /// <summary>Clears the registry entirely. Used when tearing down a headless session.</summary>
        public void Clear()
            => _registry.Clear();

        /// <summary>
        /// Releases the world: fires <see cref="Destroyed"/> (once), clears the aspect store
        /// and the registry. Safe to call twice — the second call is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Fire Destroyed before unregistering so subscribers can still query the
            // registry while unwinding — matches MonoEntity.OnDestroy / Entity.Dispose ordering.
            Destroyed?.Invoke(this);
            _store.Clear();
            _registry.Clear();

            // A world disposed while still Current would leave the static slot pointing at a
            // dead instance — the next Require/Query would silently create aspects on it or
            // iterate an empty registry without any signal that the setup is broken. Clear the
            // slot so the normal "no Current" guard kicks in. Use ForceResetCurrent rather than
            // ClearCurrent to avoid a silent no-op if Current was already swapped out.
            if (Current == this) ForceResetCurrent();
        }

        // Instance Query overloads — same functionality as the static Query<...> but scoped to
        // THIS world regardless of World.Current. Named QueryLocal to avoid the C# restriction
        // against a static and instance member sharing the same name/signature. Use for:
        //   - headless / pocket worlds that never become Current;
        //   - mini-games running alongside the main world that need to query their own registry.
        public EntityQuery<T> QueryLocal<T>() where T : class, IEntityAspect
            => new(_registry);

        public EntityQuery<T1, T2> QueryLocal<T1, T2>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            => new(_registry);

        public EntityQuery<T1, T2, T3> QueryLocal<T1, T2, T3>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            => new(_registry);

        public EntityQuery<T1, T2, T3, T4> QueryLocal<T1, T2, T3, T4>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            => new(_registry);

        public EntityQuery<T1, T2, T3, T4, T5> QueryLocal<T1, T2, T3, T4, T5>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            => new(_registry);

        public EntityQuery<T1, T2, T3, T4, T5, T6> QueryLocal<T1, T2, T3, T4, T5, T6>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            => new(_registry);

        public EntityQuery<T1, T2, T3, T4, T5, T6, T7> QueryLocal<T1, T2, T3, T4, T5, T6, T7>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            => new(_registry);

        public EntityQuery<T1, T2, T3, T4, T5, T6, T7, T8> QueryLocal<T1, T2, T3, T4, T5, T6, T7, T8>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            where T8 : class, IEntityAspect
            => new(_registry);
    }
}
