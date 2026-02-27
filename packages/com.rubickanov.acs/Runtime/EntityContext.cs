using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Shared aspect container for an entity. Lives on the root GameObject;
    /// components obtain aspects via <see cref="Require{T}"/> in Awake.
    /// </summary>
    public class EntityContext : MonoBehaviour
    {
        private readonly Dictionary<Type, object> _aspects = new();

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
            return instance;
        }

        /// <summary>
        /// Tries to get an existing aspect without creating it.
        /// </summary>
        public bool TryGet<T>(out T? aspect) where T : class, IEntityAspect
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
    }
}
