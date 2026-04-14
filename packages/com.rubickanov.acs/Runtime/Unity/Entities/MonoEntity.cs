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

        /// <summary>
        /// Allocates the entity's <see cref="Id"/> and registers it in the by-id index of
        /// <see cref="World.Current"/>, making the entity addressable via <see cref="World.TryFindById"/>
        /// immediately — before any component's first <see cref="Require{T}"/>. Derived classes
        /// overriding Awake must call <c>base.Awake()</c> or the entity will have <see cref="EntityId.None"/>
        /// and be unreachable via by-id lookup.
        /// <para/>
        /// Id allocation runs unconditionally; the by-id registration silently no-ops when
        /// <see cref="World.Current"/> is null — mirroring the existing per-aspect Register
        /// behavior in <see cref="Require{T}"/>. If no world is set at Awake time, the entity is
        /// never retroactively registered when one appears later — same invariant as the
        /// per-aspect path.
        /// </summary>
        protected virtual void Awake()
        {
            Id = EntityId.Allocate();
            World.Current?.Register(this);
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