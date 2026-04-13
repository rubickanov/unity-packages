using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Singleton entity context that doubles as the scene-wide entity registry.
    /// World is an <see cref="MonoEntity"/> itself — mark world-scoped aspects with
    /// <c>[Aspect]</c> as usual and access them via <see cref="Require{T}"/>. It also tracks
    /// every other <see cref="IEntity"/> so <see cref="Query{T}"/> can locate them by
    /// aspect type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DefaultExecutionOrder"/> is set so <c>World.Awake</c> runs before the typical
    /// <see cref="EntityComponent.Awake"/> which triggers <see cref="MonoEntity.Require{T}"/>
    /// via aspect injection. Registration is handled inline by <see cref="MonoEntity.Require{T}"/>,
    /// which calls <see cref="Register"/> whenever a <see cref="World"/> instance is alive — so
    /// <c>World</c> must exist before any entity requests its first aspect.
    /// </para>
    /// <para>
    /// Composition note: the actual registry + query factories live on a pure-C#
    /// <see cref="WorldCore"/>. The static <see cref="Query{T}"/> API here is a thin
    /// proxy so headless simulations can use the same query logic without Unity.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    public class World : SingletonMonoEntity<World>
    {
        private readonly WorldCore _core = new();

        /// <summary>Exposed for tests and diagnostics. Do not mutate directly.</summary>
        internal WorldCore Core => _core;

        /// <summary>Shorthand for the core's registry — preserved for pre-existing test call sites.</summary>
        internal EntityRegistry Registry => _core.Registry;

        protected override void OnDestroy()
        {
            _core.Clear();
            base.OnDestroy();
        }

        internal void Register(IEntity entity, System.Type aspectType)
            => _core.Register(entity, aspectType);

        internal void Unregister(IEntity entity, System.Collections.Generic.Dictionary<System.Type, object>.KeyCollection aspectTypes)
            => _core.Unregister(entity, aspectTypes);

        /// <summary>
        /// Shorthand for <c>World.Instance.Require&lt;T&gt;()</c> — fetches or creates a world-scoped aspect.
        /// Throws if no World is alive. Shadows <see cref="MonoEntity.Require{T}"/> with a static
        /// overload so <c>World.Require&lt;T&gt;()</c> reads as a global accessor.
        /// </summary>
        public new static T Require<T>() where T : class, IEntityAspect, new()
            => ((MonoEntity)GetInstanceOrThrow(nameof(Require))).Require<T>();

        /// <summary>
        /// Enumerates every aspect of type <typeparamref name="T"/> currently present in the scene.
        /// Throws <see cref="System.InvalidOperationException"/> if no <see cref="World"/> is alive —
        /// matches <see cref="Require{T}"/> so callers can't silently iterate an empty set when setup is missing.
        /// </summary>
        public static EntityQuery<T> Query<T>() where T : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T>();

        /// <summary>
        /// Enumerates every entity that carries both <typeparamref name="T1"/> and <typeparamref name="T2"/>.
        /// Throws <see cref="System.InvalidOperationException"/> if no <see cref="World"/> is alive.
        /// </summary>
        public static EntityQuery<T1, T2> Query<T1, T2>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T1, T2>();

        /// <summary>Enumerates every entity that carries all three aspect types. Throws if no World is alive.</summary>
        public static EntityQuery<T1, T2, T3> Query<T1, T2, T3>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T1, T2, T3>();

        /// <summary>Enumerates every entity that carries all four aspect types. Throws if no World is alive.</summary>
        public static EntityQuery<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T1, T2, T3, T4>();

        /// <summary>Enumerates every entity that carries all five aspect types. Throws if no World is alive.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5> Query<T1, T2, T3, T4, T5>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T1, T2, T3, T4, T5>();

        /// <summary>Enumerates every entity that carries all six aspect types. Throws if no World is alive.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6> Query<T1, T2, T3, T4, T5, T6>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T1, T2, T3, T4, T5, T6>();

        /// <summary>Enumerates every entity that carries all seven aspect types. Throws if no World is alive.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6, T7> Query<T1, T2, T3, T4, T5, T6, T7>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T1, T2, T3, T4, T5, T6, T7>();

        /// <summary>
        /// Enumerates every entity that carries all eight aspect types. Eight is the maximum
        /// overload — if you need more, define a composite aspect that aggregates the data.
        /// Throws if no World is alive.
        /// </summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6, T7, T8> Query<T1, T2, T3, T4, T5, T6, T7, T8>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            where T8 : class, IEntityAspect
            => GetInstanceOrThrow(nameof(Query))._core.Query<T1, T2, T3, T4, T5, T6, T7, T8>();

        private static World GetInstanceOrThrow(string member)
        {
            var instance = Instance;
            if (instance == null)
                throw new System.InvalidOperationException(
                    $"{nameof(World)}.{member}() called but no {nameof(World)} instance exists in the scene.");
            return instance;
        }
    }
}
