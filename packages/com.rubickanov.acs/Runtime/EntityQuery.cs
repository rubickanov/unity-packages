using System.Collections;
using System.Collections.Generic;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Enumerates every aspect of type <typeparamref name="T"/> currently registered with
    /// a <see cref="World"/>. Obtained via <see cref="World.Query{T}"/>.
    /// </summary>
    /// <remarks>
    /// Iteration yields the aspect instances themselves, matching the single-argument form
    /// documented for <see cref="World.Query{T}"/>. Use the multi-argument overloads when you
    /// need the owning <see cref="EntityContext"/> alongside the aspects.
    /// </remarks>
    public readonly struct EntityQuery<T> : IEnumerable<T> where T : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Walks every entity registered for <typeparamref name="T"/> and yields the aspect instance.
        /// Entities without the aspect (e.g. freshly destroyed between registration and iteration) are skipped.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T)))
            {
                if (entity.TryGet<T>(out var aspect))
                    yield return aspect;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates every entity that carries all of <typeparamref name="T1"/>, <typeparamref name="T2"/>.
    /// Yields tuples so callers can destructure in <c>foreach</c>.
    /// </summary>
    public readonly struct EntityQuery<T1, T2> : IEnumerable<(EntityContext Entity, T1 First, T2 Second)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Walks every entity registered for <typeparamref name="T1"/> and yields a tuple for the ones
        /// that also carry <typeparamref name="T2"/>.
        /// </summary>
        public IEnumerator<(EntityContext Entity, T1 First, T2 Second)> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T1)))
            {
                if (entity.TryGet<T1>(out var first) && entity.TryGet<T2>(out var second))
                    yield return (entity, first, second);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates every entity that carries all three aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3>
        : IEnumerable<(EntityContext Entity, T1 First, T2 Second, T3 Third)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public IEnumerator<(EntityContext Entity, T1 First, T2 Second, T3 Third)> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T1)))
            {
                if (entity.TryGet<T1>(out var a1) &&
                    entity.TryGet<T2>(out var a2) &&
                    entity.TryGet<T3>(out var a3))
                    yield return (entity, a1, a2, a3);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates every entity that carries all four aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4>
        : IEnumerable<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public IEnumerator<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth)> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T1)))
            {
                if (entity.TryGet<T1>(out var a1) &&
                    entity.TryGet<T2>(out var a2) &&
                    entity.TryGet<T3>(out var a3) &&
                    entity.TryGet<T4>(out var a4))
                    yield return (entity, a1, a2, a3, a4);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates every entity that carries all five aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5>
        : IEnumerable<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public IEnumerator<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth)> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T1)))
            {
                if (entity.TryGet<T1>(out var a1) &&
                    entity.TryGet<T2>(out var a2) &&
                    entity.TryGet<T3>(out var a3) &&
                    entity.TryGet<T4>(out var a4) &&
                    entity.TryGet<T5>(out var a5))
                    yield return (entity, a1, a2, a3, a4, a5);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates every entity that carries all six aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5, T6>
        : IEnumerable<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
        where T6 : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public IEnumerator<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth)> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T1)))
            {
                if (entity.TryGet<T1>(out var a1) &&
                    entity.TryGet<T2>(out var a2) &&
                    entity.TryGet<T3>(out var a3) &&
                    entity.TryGet<T4>(out var a4) &&
                    entity.TryGet<T5>(out var a5) &&
                    entity.TryGet<T6>(out var a6))
                    yield return (entity, a1, a2, a3, a4, a5, a6);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates every entity that carries all seven aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5, T6, T7>
        : IEnumerable<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
        where T6 : class, IEntityAspect
        where T7 : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public IEnumerator<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh)> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T1)))
            {
                if (entity.TryGet<T1>(out var a1) &&
                    entity.TryGet<T2>(out var a2) &&
                    entity.TryGet<T3>(out var a3) &&
                    entity.TryGet<T4>(out var a4) &&
                    entity.TryGet<T5>(out var a5) &&
                    entity.TryGet<T6>(out var a6) &&
                    entity.TryGet<T7>(out var a7))
                    yield return (entity, a1, a2, a3, a4, a5, a6, a7);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates every entity that carries all eight aspect types. If you need more, compose a
    /// dedicated aspect that aggregates the data instead — eight is the maximum overload provided.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5, T6, T7, T8>
        : IEnumerable<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
        where T6 : class, IEntityAspect
        where T7 : class, IEntityAspect
        where T8 : class, IEntityAspect
    {
        private readonly EntityRegistry? _registry;

        internal EntityQuery(EntityRegistry? registry)
        {
            _registry = registry;
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public IEnumerator<(EntityContext Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth)> GetEnumerator()
        {
            if (_registry == null)
                yield break;
            foreach (var entity in _registry.GetAllWith(typeof(T1)))
            {
                if (entity.TryGet<T1>(out var a1) &&
                    entity.TryGet<T2>(out var a2) &&
                    entity.TryGet<T3>(out var a3) &&
                    entity.TryGet<T4>(out var a4) &&
                    entity.TryGet<T5>(out var a5) &&
                    entity.TryGet<T6>(out var a6) &&
                    entity.TryGet<T7>(out var a7) &&
                    entity.TryGet<T8>(out var a8))
                    yield return (entity, a1, a2, a3, a4, a5, a6, a7, a8);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
