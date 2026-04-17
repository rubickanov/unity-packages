using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Shared aspect container for an entity. Lives on the root GameObject;
    /// components obtain aspects via <see cref="Require{T}"/> in Awake.
    /// </summary>
    // Unity does not guarantee Awake ordering between parent and child GameObjects. Without an
    // explicit execution order, a child <see cref="EntityComponent"/> can Awake — and therefore
    // call <see cref="Require{T}"/> — before this MonoEntity has allocated its <see cref="Id"/>
    // or registered itself in the world. Subscribers to World.AspectCreated that key by Id then
    // receive EntityId.None. -999 keeps MonoEntity running after MonoWorld (-1000) so
    // World.Current is always available, but before any user EntityComponent.
    [DefaultExecutionOrder(-999)]
    public class MonoEntity : MonoBehaviour, IEntity
    {
        /// <summary>
        /// Raised once per entity in <c>Start</c>, after every component's <c>Awake</c> has run.
        /// Intended as an "initial setup done" lifecycle stamp for extension packages
        /// (e.g. ACS.Netcode). Does NOT guarantee that every aspect the entity will ever have is
        /// present — aspects created lazily via <see cref="Require{T}"/> after Start (e.g. from
        /// <c>OnEnable</c>, <c>Update</c>, or delayed logic) arrive later. For reacting to those,
        /// subscribe to <see cref="OnAspectCreated"/> instead.
        /// Cleared each play session via <see cref="ResetStaticEvents"/>.
        /// </summary>
        public static event Action<MonoEntity>? OnAwakeCompleted;

        /// <summary>
        /// Raised once for every new aspect instance created on any entity living in the current
        /// <see cref="World"/> — including pure-C# <see cref="Entity"/> instances, not just
        /// <see cref="MonoEntity"/>. Fires for aspects created during Awake-time injection and
        /// for those created lazily later. Does not fire when <c>Require</c> returns an
        /// already-existing aspect, nor when a <see cref="MonoEntity"/> runs <c>Require</c>
        /// without a <see cref="World.Current"/> — the event is scoped to "new aspect
        /// reachable via world queries", which is meaningless without a world.
        /// Wired via <c>MonoWorld</c>'s forwarder from <see cref="World.AspectCreated"/>.
        /// Cleared each play session via <see cref="ResetStaticEvents"/>.
        /// </summary>
        public static event Action<IEntity, Type>? OnAspectCreated;

        /// <inheritdoc/>
        public EntityId Id { get; private set; }

        /// <inheritdoc/>
        public event Action<IEntity>? Destroyed;

        private readonly AspectStore _store = new();

        // Reset static event subscribers at the start of every play session so subscriptions from
        // InitializeOnLoad-based extensions don't pile up when the user disables Domain Reload
        // in Project Settings → Enter Play Mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEvents()
        {
            OnAwakeCompleted = null;
            OnAspectCreated = null;
        }

        // Set when Awake ran with no World.Current; cleared either when a world becomes current
        // (retroactive register path) or when OnDestroy unsubscribes a still-pending handler.
        // The cached delegate lets us unsubscribe — a fresh `OnWorldBecameCurrent` method group
        // would allocate a new Action each reference and not match the original for -=.
        private Action<World>? _pendingWorldHandler;

        /// <summary>
        /// Allocates the entity's <see cref="Id"/> and registers it in the by-id index of
        /// <see cref="World.Current"/>, making the entity addressable via <see cref="World.TryFindById"/>
        /// immediately — before any component's first <see cref="Require{T}"/>. Derived classes
        /// overriding Awake must call <c>base.Awake()</c> or the entity will have <see cref="EntityId.None"/>
        /// and be unreachable via by-id lookup.
        /// <para/>
        /// Id allocation runs unconditionally. When no <see cref="World.Current"/> is assigned at
        /// Awake time, the entity defers registration: it subscribes to <see cref="World.CurrentChanged"/>
        /// and registers itself — together with every aspect it has accumulated in the meantime —
        /// the moment a world takes over. That handles the "spawn entity into a scene without a
        /// MonoWorld, then drop a MonoWorld later" case that was previously a silent invariant leak.
        /// </summary>
        protected virtual void Awake()
        {
            Id = EntityId.Allocate();
            var current = World.Current;
            if (current != null)
            {
                current.Register(this);
                return;
            }

            // No world yet — wait for one via CurrentChanged. Store the delegate so OnDestroy
            // can unsubscribe if this entity dies before any world appears (scene torn down,
            // object destroyed before a MonoWorld is ever added).
            _pendingWorldHandler = OnWorldBecameCurrent;
            World.CurrentChanged += _pendingWorldHandler;
        }

        private void OnWorldBecameCurrent(World world)
        {
            // Unsubscribe immediately — we only need the first transition. A later ClearCurrent
            // / SetCurrent cycle shouldn't re-register an entity that's already in the registry,
            // and it shouldn't re-fire AspectCreated for aspects that have already been announced.
            if (_pendingWorldHandler != null)
            {
                World.CurrentChanged -= _pendingWorldHandler;
                _pendingWorldHandler = null;
            }

            world.Register(this);
            // Flow every aspect built during the no-world window into the world's per-aspect
            // buckets. World.Register(entity, aspectType) also fires AspectCreated, so
            // acs.netcode / acs.persistence subscribers see these aspects exactly as if the
            // entity had been created with the world already in place — the aspect instances
            // themselves are the same, but from the world's point of view they're "new arrivals".
            foreach (var aspectType in _store.AspectTypes)
                world.Register(this, aspectType);
        }

        /// <summary>
        /// Returns the aspect of type <typeparamref name="T"/>, creating it if it doesn't exist yet.
        /// When a <see cref="World.Current"/> is assigned, the aspect-creation notification flows
        /// through <see cref="World.AspectCreated"/> → <c>MonoWorld</c>'s forwarder →
        /// <see cref="OnAspectCreated"/>. Without a world, no notification fires — the event is
        /// scoped to "new aspect reachable via world queries", which is meaningless without a world.
        /// </summary>
        public virtual T Require<T>() where T : class, IEntityAspect, new()
        {
            var instance = _store.GetOrAdd<T>(out var created);
            if (created) World.Current?.Register(this, typeof(T));
            return instance;
        }

        /// <summary>
        /// Tries to get an existing aspect without creating it.
        /// </summary>
        public virtual bool TryGet<T>([NotNullWhen(returnValue: true)] out T? aspect) where T : class, IEntityAspect
            => _store.TryGet(out aspect);

        /// <summary>
        /// Returns true if the aspect of type <typeparamref name="T"/> has been created.
        /// </summary>
        public virtual bool Has<T>() where T : class, IEntityAspect => _store.Has<T>();

        /// <summary>
        /// Returns all aspect instances currently registered on this entity.
        /// </summary>
        public virtual IEnumerable<object> GetAllAspects() => _store.GetAllAspects();

        /// <inheritdoc/>
        public virtual Dictionary<Type, object>.KeyCollection AspectTypes => _store.AspectTypes;

        /// <summary>
        /// Internal trampoline so <c>MonoWorld</c> can forward pure-<see cref="World"/>
        /// <c>AspectCreated</c> events into the public <see cref="OnAspectCreated"/> event
        /// without exposing the event itself as writeable outside the class.
        /// </summary>
        internal static void InvokeOnAspectCreated(IEntity entity, Type aspectType)
            => OnAspectCreated?.Invoke(entity, aspectType);

        private void Start()
        {
            OnAwakeCompleted?.Invoke(this);
        }

        protected virtual void OnDestroy()
        {
            // If we're still waiting for a world, drop the subscription so a world that becomes
            // current after this entity's destruction cannot invoke a handler on a dead object.
            // This also covers entity-destroyed-before-any-world-appeared, where the per-aspect
            // Unregister calls below are no-ops anyway.
            if (_pendingWorldHandler != null)
            {
                World.CurrentChanged -= _pendingWorldHandler;
                _pendingWorldHandler = null;
            }
            // Fire before unregistering so subscribers can still query the world
            // or the registry while unwinding their own state.
            Destroyed?.Invoke(this);
            // Mirror construction order (id-first on register → id-last on unregister): clear the
            // per-aspect buckets first, then the by-id slot last, so any cascade triggered by the
            // per-aspect Unregister can still resolve this entity via TryFindById.
            World.Current?.Unregister(this, _store.AspectTypes);
            World.Current?.Unregister(this);
        }
    }
}