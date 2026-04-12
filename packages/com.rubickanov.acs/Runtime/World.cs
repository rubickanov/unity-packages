using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Singleton entity context that doubles as the scene-wide entity registry.
    /// World is an <see cref="EntityContext"/> itself — mark world-scoped aspects with
    /// <c>[Aspect]</c> as usual and access them via <see cref="Require{T}"/>. It also tracks
    /// every other <see cref="EntityContext"/> so <see cref="Query{T}"/> can locate them by
    /// aspect type.
    /// </summary>
    /// <remarks>
    /// <see cref="DefaultExecutionOrder"/> is set so <c>World.Awake</c> runs before the typical
    /// <see cref="EntityComponent.Awake"/> which triggers <see cref="EntityContext.Require{T}"/>
    /// via aspect injection. A post-assignment scan in <see cref="Awake"/> also picks up any
    /// entity that already created aspects before the World existed (additive scene loads, etc).
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    public class World : SingletonEntityContext<World>
    {
        private readonly EntityRegistry _registry = new();

        /// <summary>Exposed for tests and diagnostics. Do not mutate directly.</summary>
        internal EntityRegistry Registry => _registry;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
                return;

            // Safety net: pick up entities that created aspects before this World existed.
#if UNITY_2023_1_OR_NEWER
            var contexts = Object.FindObjectsByType<EntityContext>(FindObjectsSortMode.None);
#else
            var contexts = Object.FindObjectsOfType<EntityContext>();
#endif
            for (int i = 0; i < contexts.Length; i++)
            {
                var context = contexts[i];
                if (context == this)
                    continue;
                foreach (var aspect in context.GetAllAspects())
                    _registry.Register(context, aspect.GetType());
            }
        }

        protected override void OnDestroy()
        {
            _registry.Clear();
            base.OnDestroy();
        }

        internal void Register(EntityContext entity, System.Type aspectType)
            => _registry.Register(entity, aspectType);

        internal void Unregister(EntityContext entity)
            => _registry.Unregister(entity);

        /// <summary>
        /// Shorthand for <c>World.Instance.Require&lt;T&gt;()</c> — fetches or creates a world-scoped aspect.
        /// Throws if no World is alive. Shadows <see cref="EntityContext.Require{T}"/> with a static
        /// overload so <c>World.Require&lt;T&gt;()</c> reads as a global accessor.
        /// </summary>
        public new static T Require<T>() where T : class, IEntityAspect, new()
        {
            if (Instance == null)
                throw new System.InvalidOperationException(
                    $"{nameof(World)}.{nameof(Require)}<{typeof(T).Name}>() called but no {nameof(World)} instance exists in the scene.");
            return ((EntityContext)Instance).Require<T>();
        }

        /// <summary>
        /// Enumerates every aspect of type <typeparamref name="T"/> currently present in the scene.
        /// Returns an empty query if no <see cref="World"/> is alive.
        /// </summary>
        public static EntityQuery<T> Query<T>() where T : class, IEntityAspect
            => new(Instance != null ? Instance._registry : null);

        /// <summary>
        /// Enumerates every entity that carries both <typeparamref name="T1"/> and <typeparamref name="T2"/>.
        /// Returns an empty query if no <see cref="World"/> is alive.
        /// </summary>
        public static EntityQuery<T1, T2> Query<T1, T2>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            => new(Instance != null ? Instance._registry : null);

        /// <summary>Enumerates every entity that carries all three aspect types.</summary>
        public static EntityQuery<T1, T2, T3> Query<T1, T2, T3>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            => new(Instance != null ? Instance._registry : null);

        /// <summary>Enumerates every entity that carries all four aspect types.</summary>
        public static EntityQuery<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            => new(Instance != null ? Instance._registry : null);

        /// <summary>Enumerates every entity that carries all five aspect types.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5> Query<T1, T2, T3, T4, T5>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            => new(Instance != null ? Instance._registry : null);

        /// <summary>Enumerates every entity that carries all six aspect types.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6> Query<T1, T2, T3, T4, T5, T6>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            => new(Instance != null ? Instance._registry : null);

        /// <summary>Enumerates every entity that carries all seven aspect types.</summary>
        public static EntityQuery<T1, T2, T3, T4, T5, T6, T7> Query<T1, T2, T3, T4, T5, T6, T7>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            => new(Instance != null ? Instance._registry : null);

        /// <summary>
        /// Enumerates every entity that carries all eight aspect types. Eight is the maximum
        /// overload — if you need more, define a composite aspect that aggregates the data.
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
            => new(Instance != null ? Instance._registry : null);
    }
}
