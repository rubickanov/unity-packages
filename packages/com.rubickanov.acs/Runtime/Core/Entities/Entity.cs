using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Pure-C# aspect container. Use when an entity should NOT be tied to a
    /// Unity <c>GameObject</c>: pocket entities (an item inside an inventory, a
    /// buff with no visual), headless simulations (server authority running in
    /// a console host), or fast edit-mode tests that exercise aspect logic
    /// without booting the Unity player loop.
    /// <para/>
    /// Pass a <see cref="World"/> to <see cref="Entity(World)"/> to opt
    /// into automatic Register/Unregister — the same way <see cref="MonoEntity"/>
    /// auto-integrates with <see cref="World.Current"/>. The parameterless ctor
    /// keeps the entity standalone for callers that want to drive registration themselves.
    /// <para/>
    /// The lifetime is managed explicitly via <see cref="Dispose"/>. Dispose is
    /// idempotent: the <see cref="Destroyed"/> event fires at most once, and the
    /// aspect dictionary is cleared so subscribers observe an empty entity.
    /// <para/>
    /// <b>Thread safety:</b> not thread-safe. <see cref="Require{T}"/> delegates to
    /// <see cref="AspectStore"/>, which can drop instances under concurrent access
    /// (see its remarks). <see cref="Dispose"/> is also unprotected — racing a Dispose
    /// with a <c>Require</c> can produce an aspect on a dying entity. Callers in
    /// multi-threaded headless contexts must serialize per-entity access externally.
    /// </summary>
    public sealed class Entity : IEntity, IDisposable
    {
        /// <inheritdoc/>
        public EntityId Id { get; } = EntityId.Allocate();

        /// <inheritdoc/>
        public event Action<IEntity>? Destroyed;

        private readonly AspectStore _store = new();
        private readonly World? _world;
        private bool _disposed;

        /// <summary>
        /// Creates a standalone pure-C# entity with no registry integration. The entity still
        /// gets a unique <see cref="Id"/>, but is not findable via any <see cref="World.TryFindById"/>
        /// until the caller registers it manually. Prefer <see cref="Entity(World)"/> for entities
        /// that should participate in world-scoped queries and by-id lookup.
        /// </summary>
        public Entity()
        {
        }

        /// <summary>
        /// Creates a pure-C# entity that auto-registers with <paramref name="world"/>:
        /// immediately in the by-id index (findable via <see cref="World.TryFindById"/> right after
        /// construction, before any <see cref="Require{T}"/>), and lazily per-aspect on every
        /// first <see cref="Require{T}"/>. Auto-unregisters both on <see cref="Dispose"/>.
        /// Mirrors the implicit <see cref="MonoEntity"/> ↔ <see cref="World.Current"/> integration
        /// so pure-core consumers do not have to mirror every <c>Require</c> with a manual
        /// <c>Register</c>.
        /// </summary>
        public Entity(World world)
        {
            _world = world;
            _world.Register(this);
        }

        /// <inheritdoc/>
        public T Require<T>() where T : class, IEntityAspect, new()
        {
            var instance = _store.GetOrAdd<T>(out var created);
            if (created) _world?.Register(this, typeof(T));
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
        /// Releases the entity: fires <see cref="Destroyed"/> (once) and clears
        /// the aspect dictionary. Safe to call twice — second call is a no-op.
        /// Subscribing to <see cref="Destroyed"/> after Dispose is legal but
        /// silently inert — the event will never fire again.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Fire Destroyed before unregistering so subscribers can still query
            // the registry while unwinding — matches MonoEntity.OnDestroy ordering.
            Destroyed?.Invoke(this);
            // Teardown order mirrors construction: per-aspect registration was lazy (from Require),
            // id-registration was immediate in the ctor — so unregister the per-aspect buckets
            // first, then drop the by-id entry last. This keeps the id slot live long enough for
            // any cascade triggered by per-aspect Unregister to still resolve the entity.
            _world?.Unregister(this, _store.AspectTypes);
            _world?.Unregister(this);
            _store.Clear();
        }
    }
}
