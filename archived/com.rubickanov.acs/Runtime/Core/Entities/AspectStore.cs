using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Pure-C# aspect storage shared by <see cref="Entity"/> and <see cref="MonoEntity"/>.
    /// Holds the <c>Type → aspect</c> dictionary and the read-only lookup surface;
    /// registration side-effects (world wiring, events) stay with the owner so each
    /// <c>Require</c> call site makes its own consequences visible.
    /// <para/>
    /// Callers obtain an aspect via <see cref="GetOrAdd{T}"/> and inspect
    /// <paramref name="created"/> to decide whether to fire their own side-effects.
    /// <para/>
    /// <b>Thread safety:</b> not thread-safe. Two threads calling <see cref="GetOrAdd{T}"/>
    /// concurrently can each construct a fresh <typeparamref name="T"/> and the later
    /// writer wins in the dictionary — both callers get different instances, but only one
    /// of them is retained. Callers in multi-threaded contexts (headless simulation,
    /// background jobs) must serialize access externally.
    /// </summary>
    public sealed class AspectStore
    {
        private readonly Dictionary<Type, object> _aspects = new();

        /// <summary>
        /// Returns the existing aspect of type <typeparamref name="T"/> or creates
        /// and stores a new one. <paramref name="created"/> is <c>true</c> iff a
        /// new instance was produced on this call — owners use it to gate their
        /// registration/event side-effects.
        /// </summary>
        public T GetOrAdd<T>(out bool created) where T : class, IEntityAspect, new()
        {
            var type = typeof(T);
            if (_aspects.TryGetValue(type, out var existing))
            {
                created = false;
                return (T)existing;
            }

            var instance = new T();
            _aspects[type] = instance;
            created = true;
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
        /// Returns all aspect instances currently stored. The result is a snapshot
        /// so callers may mutate the store (e.g. <see cref="GetOrAdd{T}"/> another
        /// aspect) while iterating. Cost: one <c>object[]</c> per call.
        /// </summary>
        public IEnumerable<object> GetAllAspects()
        {
            var snapshot = new object[_aspects.Count];
            _aspects.Values.CopyTo(snapshot, 0);
            return snapshot;
        }

        /// <summary>
        /// The concrete <see cref="Dictionary{TKey,TValue}.KeyCollection"/> of
        /// aspect types. The exact type (not <c>IEnumerable&lt;Type&gt;</c>) is
        /// load-bearing: <c>EntityRegistry.Unregister</c> iterates it via the
        /// struct enumerator for zero-alloc teardown.
        /// </summary>
        public Dictionary<Type, object>.KeyCollection AspectTypes => _aspects.Keys;

        /// <summary>
        /// Drops all stored aspects. Used by <see cref="Entity.Dispose"/> so
        /// post-dispose observers see an empty entity.
        /// </summary>
        public void Clear()
        {
            _aspects.Clear();
        }
    }
}
