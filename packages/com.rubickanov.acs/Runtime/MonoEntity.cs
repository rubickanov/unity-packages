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
    [MovedFrom(true, sourceNamespace: "Rubickanov.ACS.Runtime", sourceAssembly: "ACS.Runtime", sourceClassName: "EntityContext")]
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
        /// Raised once for every new aspect instance created on any entity, immediately after
        /// registration with <see cref="World"/>. Fires from <see cref="Require{T}"/>, both for
        /// aspects created during Awake-time injection and for those created lazily later. Does
        /// not fire when <c>Require</c> returns an already-existing aspect.
        /// Cleared each play session via <see cref="ResetStaticEvents"/>.
        /// </summary>
        public static event Action<IEntity, Type>? OnAspectCreated;

        /// <inheritdoc/>
        public event Action<IEntity>? Destroyed;

        private readonly Dictionary<Type, object> _aspects = new();

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
        /// Hook for derived classes (e.g. <see cref="SingletonMonoEntity{T}"/>) to run
        /// initialization before Start. Empty by default — the base class has no Awake behavior of its own.
        /// </summary>
        protected virtual void Awake()
        {
        }

        /// <summary>
        /// Returns the aspect of type <typeparamref name="T"/>, creating it if it doesn't exist yet.
        /// </summary>
        public T Require<T>() where T : class, IEntityAspect, new()
        {
            var type = typeof(T);
            if (_aspects.TryGetValue(type, out var existing))
                return (T)existing;
            var instance = new T();
            _aspects[type] = instance;
            World.Instance?.Register(this, type);
            OnAspectCreated?.Invoke(this, type);
            return instance;
        }

        /// <summary>
        /// Tries to get an existing aspect without creating it.
        /// </summary>
        public bool TryGet<T>([NotNullWhen(returnValue: true)] out T? aspect) where T : class, IEntityAspect
        {
            if (_aspects.TryGetValue(typeof(T), out var existing))
            {
                aspect = (T)existing;
                return true;
            }
            aspect = null;
            return false;
        }

        /// <summary>
        /// Returns true if the aspect of type <typeparamref name="T"/> has been created.
        /// </summary>
        public bool Has<T>() where T : class, IEntityAspect
        {
            return _aspects.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Returns all aspect instances currently registered on this entity.
        /// </summary>
        public IEnumerable<object> GetAllAspects()
        {
            // Snapshot so callers can safely mutate _aspects (e.g. Require another
            // aspect) while iterating. Cost: one object[] per call.
            var snapshot = new object[_aspects.Count];
            _aspects.Values.CopyTo(snapshot, 0);
            return snapshot;
        }

        /// <inheritdoc/>
        public Dictionary<Type, object>.KeyCollection AspectTypes => _aspects.Keys;

        private void Start()
        {
            OnAwakeCompleted?.Invoke(this);
        }

        protected virtual void OnDestroy()
        {
            // Fire before unregistering so subscribers can still query the world
            // or the registry while unwinding their own state.
            Destroyed?.Invoke(this);
            World.Instance?.Unregister(this, _aspects.Keys);
        }
    }
}
