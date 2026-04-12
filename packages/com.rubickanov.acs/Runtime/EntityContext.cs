using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Shared aspect container for an entity. Lives on the root GameObject;
    /// components obtain aspects via <see cref="Require{T}"/> in Awake.
    /// </summary>
    public class EntityContext : MonoBehaviour
    {
        /// <summary>
        /// Raised once in Start after all Awake calls have completed and aspects have been created.
        /// Used by extension packages (e.g. netcode) to perform auto-setup.
        /// </summary>
        public static event Action<EntityContext>? OnContextInitialized;

        private readonly Dictionary<Type, object> _aspects = new();

        /// <summary>
        /// Hook for derived classes (e.g. <see cref="SingletonEntityContext{T}"/>) to run
        /// initialization before Start. Keep the base class free of Awake behavior so aspects
        /// remain lazy — callers only pay for what they use.
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
        public IEnumerable<object> GetAllAspects() => _aspects.Values;

        private void Start()
        {
            OnContextInitialized?.Invoke(this);
        }

        protected virtual void OnDestroy()
        {
            World.Instance?.Unregister(this);
        }
    }
}
