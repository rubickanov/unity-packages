using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Pure-C# contract for an aspect container. Implemented by the Unity-bound
    /// <see cref="MonoEntity"/> (tied to a GameObject's lifetime) and the pure
    /// POCO <see cref="Entity"/> (for pocket entities, headless simulation, and
    /// edit-mode unit tests).
    /// <para/>
    /// Anything that only needs to read/write aspects or subscribe to per-entity
    /// lifecycle (via <see cref="Destroyed"/>) should depend on this interface
    /// rather than on <see cref="MonoEntity"/>, so it can run without Unity.
    /// </summary>
    public interface IEntity
    {
        /// <summary>
        /// Raised exactly once when the entity is destroyed:
        /// - <see cref="MonoEntity"/> fires it from <c>OnDestroy</c>
        /// - <see cref="Entity"/> fires it from <see cref="IDisposable.Dispose"/>
        /// Subscribers receive the entity instance for convenience. Subscribing
        /// after the entity is destroyed is legal but silently inert — the event
        /// will not fire again.
        /// </summary>
        event Action<IEntity>? Destroyed;

        /// <summary>
        /// Returns the aspect of type <typeparamref name="T"/>, creating it if
        /// it doesn't exist yet. Idempotent — repeated calls return the same
        /// instance.
        /// </summary>
        T Require<T>() where T : class, IEntityAspect, new();

        /// <summary>
        /// Tries to get an existing aspect without creating it.
        /// </summary>
        bool TryGet<T>([NotNullWhen(returnValue: true)] out T? aspect) where T : class, IEntityAspect;

        /// <summary>
        /// Returns true if the aspect of type <typeparamref name="T"/> has been
        /// created on this entity.
        /// </summary>
        bool Has<T>() where T : class, IEntityAspect;

        /// <summary>
        /// Enumerates every aspect instance currently registered on this entity.
        /// Used by replication, persistence and inspector tooling that needs to
        /// scan the full aspect set.
        /// </summary>
        IEnumerable<object> GetAllAspects();

        /// <summary>
        /// The set of aspect types currently registered on this entity. Exposed as the
        /// concrete <see cref="Dictionary{TKey,TValue}.KeyCollection"/> so callers iterate
        /// with the struct enumerator and do not allocate — used by
        /// <see cref="EntityRegistry.Unregister"/> to touch only the buckets this entity
        /// actually belongs to.
        /// </summary>
        Dictionary<Type, object>.KeyCollection AspectTypes { get; }
    }
}
