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
    /// need the owning <see cref="IEntity"/> alongside the aspects.
    /// Zero-alloc on the <c>foreach</c> hot path: both the query and its nested
    /// <see cref="Enumerator"/> are value types, iteration runs over
    /// <see cref="HashSet{T}.Enumerator"/> directly, and <c>foreach</c> binds to the duck-typed
    /// <see cref="GetEnumerator"/> in preference to the <see cref="IEnumerable{T}"/> interface.
    /// The interface is kept on the type so LINQ / tooling can still consume queries — those
    /// call sites opt into boxing explicitly.
    /// </remarks>
    public readonly struct EntityQuery<T> : IEnumerable<T> where T : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T));
        }

        /// <summary>
        /// Walks every entity registered for <typeparamref name="T"/> and yields the aspect instance.
        /// Entities without the aspect (e.g. freshly destroyed between registration and iteration) are skipped.
        /// </summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private T _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default!;
            }

            public T Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    if (_inner.Current.TryGet<T>(out var aspect))
                    {
                        _current = aspect;
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Enumerates every entity that carries all of <typeparamref name="T1"/>, <typeparamref name="T2"/>.
    /// Yields tuples so callers can destructure in <c>foreach</c>.
    /// </summary>
    public readonly struct EntityQuery<T1, T2> : IEnumerable<(IEntity Entity, T1 First, T2 Second)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T1));
        }

        /// <summary>
        /// Walks every entity registered for <typeparamref name="T1"/> and yields a tuple for the ones
        /// that also carry <typeparamref name="T2"/>.
        /// </summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<(IEntity Entity, T1 First, T2 Second)> IEnumerable<(IEntity Entity, T1 First, T2 Second)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<(IEntity Entity, T1 First, T2 Second)>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private (IEntity Entity, T1 First, T2 Second) _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default;
            }

            public (IEntity Entity, T1 First, T2 Second) Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    var e = _inner.Current;
                    if (e.TryGet<T1>(out var a1) && e.TryGet<T2>(out var a2))
                    {
                        _current = (e, a1, a2);
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Enumerates every entity that carries all three aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3>
        : IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T1));
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third)> IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third)>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private (IEntity Entity, T1 First, T2 Second, T3 Third) _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default;
            }

            public (IEntity Entity, T1 First, T2 Second, T3 Third) Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    var e = _inner.Current;
                    if (e.TryGet<T1>(out var a1) &&
                        e.TryGet<T2>(out var a2) &&
                        e.TryGet<T3>(out var a3))
                    {
                        _current = (e, a1, a2, a3);
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Enumerates every entity that carries all four aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4>
        : IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T1));
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth)> IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth)>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth) _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default;
            }

            public (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth) Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    var e = _inner.Current;
                    if (e.TryGet<T1>(out var a1) &&
                        e.TryGet<T2>(out var a2) &&
                        e.TryGet<T3>(out var a3) &&
                        e.TryGet<T4>(out var a4))
                    {
                        _current = (e, a1, a2, a3, a4);
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Enumerates every entity that carries all five aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5>
        : IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T1));
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth)> IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth)>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth) _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default;
            }

            public (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth) Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    var e = _inner.Current;
                    if (e.TryGet<T1>(out var a1) &&
                        e.TryGet<T2>(out var a2) &&
                        e.TryGet<T3>(out var a3) &&
                        e.TryGet<T4>(out var a4) &&
                        e.TryGet<T5>(out var a5))
                    {
                        _current = (e, a1, a2, a3, a4, a5);
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Enumerates every entity that carries all six aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5, T6>
        : IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
        where T6 : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T1));
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth)> IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth)>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth) _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default;
            }

            public (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth) Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    var e = _inner.Current;
                    if (e.TryGet<T1>(out var a1) &&
                        e.TryGet<T2>(out var a2) &&
                        e.TryGet<T3>(out var a3) &&
                        e.TryGet<T4>(out var a4) &&
                        e.TryGet<T5>(out var a5) &&
                        e.TryGet<T6>(out var a6))
                    {
                        _current = (e, a1, a2, a3, a4, a5, a6);
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Enumerates every entity that carries all seven aspect types.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5, T6, T7>
        : IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
        where T6 : class, IEntityAspect
        where T7 : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T1));
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh)> IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh)>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh) _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default;
            }

            public (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh) Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    var e = _inner.Current;
                    if (e.TryGet<T1>(out var a1) &&
                        e.TryGet<T2>(out var a2) &&
                        e.TryGet<T3>(out var a3) &&
                        e.TryGet<T4>(out var a4) &&
                        e.TryGet<T5>(out var a5) &&
                        e.TryGet<T6>(out var a6) &&
                        e.TryGet<T7>(out var a7))
                    {
                        _current = (e, a1, a2, a3, a4, a5, a6, a7);
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Enumerates every entity that carries all eight aspect types. If you need more, compose a
    /// dedicated aspect that aggregates the data instead — eight is the maximum overload provided.
    /// </summary>
    public readonly struct EntityQuery<T1, T2, T3, T4, T5, T6, T7, T8>
        : IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth)>
        where T1 : class, IEntityAspect
        where T2 : class, IEntityAspect
        where T3 : class, IEntityAspect
        where T4 : class, IEntityAspect
        where T5 : class, IEntityAspect
        where T6 : class, IEntityAspect
        where T7 : class, IEntityAspect
        where T8 : class, IEntityAspect
    {
        private readonly HashSet<IEntity>? _bucket;

        internal EntityQuery(EntityRegistry? registry)
        {
            _bucket = registry?.GetBucketOrNull(typeof(T1));
        }

        /// <summary>Walks <typeparamref name="T1"/>'s bucket and yields entities that also carry the remaining types.</summary>
        public Enumerator GetEnumerator() => new(_bucket);

        IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth)> IEnumerable<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth)>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<(IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth)>
        {
            private HashSet<IEntity>.Enumerator _inner;
            private readonly bool _hasBucket;
            private (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth) _current;

            internal Enumerator(HashSet<IEntity>? bucket)
            {
                _hasBucket = bucket != null;
                _inner = bucket?.GetEnumerator() ?? default;
                _current = default;
            }

            public (IEntity Entity, T1 First, T2 Second, T3 Third, T4 Fourth, T5 Fifth, T6 Sixth, T7 Seventh, T8 Eighth) Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (!_hasBucket) return false;
                while (_inner.MoveNext())
                {
                    var e = _inner.Current;
                    if (e.TryGet<T1>(out var a1) &&
                        e.TryGet<T2>(out var a2) &&
                        e.TryGet<T3>(out var a3) &&
                        e.TryGet<T4>(out var a4) &&
                        e.TryGet<T5>(out var a5) &&
                        e.TryGet<T6>(out var a6) &&
                        e.TryGet<T7>(out var a7) &&
                        e.TryGet<T8>(out var a8))
                    {
                        _current = (e, a1, a2, a3, a4, a5, a6, a7, a8);
                        return true;
                    }
                }
                return false;
            }

            public void Dispose() => _inner.Dispose();
            public void Reset() => throw new System.NotSupportedException();
        }
    }
}
