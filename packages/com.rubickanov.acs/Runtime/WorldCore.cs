namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Pure-C# registry + query surface. Owns the <see cref="EntityRegistry"/> and
    /// produces <see cref="EntityQuery{T}"/> values against it. The Unity-bound
    /// <see cref="World"/> singleton composes a <see cref="WorldCore"/> and
    /// proxies its static API into the instance — headless simulations, pocket
    /// containers, and edit-mode tests can instead construct and drive
    /// <see cref="WorldCore"/> directly with no <c>MonoBehaviour</c> on the stack.
    /// </summary>
    /// <remarks>
    /// Register/Unregister accept <see cref="IEntity"/>, so both
    /// <see cref="MonoEntity"/> and pure POCO <see cref="Entity"/> instances can
    /// participate in the same world.
    /// </remarks>
    public sealed class WorldCore
    {
        private readonly EntityRegistry _registry = new();

        /// <summary>Exposed for tests and tooling. Do not mutate directly.</summary>
        public EntityRegistry Registry => _registry;

        /// <summary>
        /// Records that <paramref name="entity"/> carries an aspect of type
        /// <paramref name="aspectType"/>. Called by <see cref="MonoEntity.Require{T}"/>;
        /// pure-core consumers can call this directly after creating an aspect.
        /// </summary>
        public void Register(IEntity entity, System.Type aspectType)
            => _registry.Register(entity, aspectType);

        /// <summary>
        /// Drops <paramref name="entity"/> from each bucket listed in <paramref name="aspectTypes"/>.
        /// Pass <see cref="IEntity.AspectTypes"/> so only the buckets the entity actually
        /// belongs to are touched. Safe for aspect types that were never registered.
        /// </summary>
        public void Unregister(IEntity entity, System.Collections.Generic.Dictionary<System.Type, object>.KeyCollection aspectTypes)
            => _registry.Unregister(entity, aspectTypes);

        /// <summary>Clears the registry entirely. Used when tearing down a headless session.</summary>
        public void Clear()
            => _registry.Clear();

        /// <summary>
        /// Enumerates every aspect of type <typeparamref name="T"/> currently registered with this core.
        /// </summary>
        public EntityQuery<T> Query<T>() where T : class, IEntityAspect
            => new(_registry);

        /// <summary>Enumerates every entity carrying both aspect types.</summary>
        public EntityQuery<T1, T2> Query<T1, T2>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            => new(_registry);

        /// <summary>Enumerates every entity carrying all three aspect types.</summary>
        public EntityQuery<T1, T2, T3> Query<T1, T2, T3>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            => new(_registry);

        /// <summary>Enumerates every entity carrying all four aspect types.</summary>
        public EntityQuery<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            => new(_registry);

        /// <summary>Enumerates every entity carrying all five aspect types.</summary>
        public EntityQuery<T1, T2, T3, T4, T5> Query<T1, T2, T3, T4, T5>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            => new(_registry);

        /// <summary>Enumerates every entity carrying all six aspect types.</summary>
        public EntityQuery<T1, T2, T3, T4, T5, T6> Query<T1, T2, T3, T4, T5, T6>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            => new(_registry);

        /// <summary>Enumerates every entity carrying all seven aspect types.</summary>
        public EntityQuery<T1, T2, T3, T4, T5, T6, T7> Query<T1, T2, T3, T4, T5, T6, T7>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            => new(_registry);

        /// <summary>Enumerates every entity carrying all eight aspect types. Maximum arity.</summary>
        public EntityQuery<T1, T2, T3, T4, T5, T6, T7, T8> Query<T1, T2, T3, T4, T5, T6, T7, T8>()
            where T1 : class, IEntityAspect
            where T2 : class, IEntityAspect
            where T3 : class, IEntityAspect
            where T4 : class, IEntityAspect
            where T5 : class, IEntityAspect
            where T6 : class, IEntityAspect
            where T7 : class, IEntityAspect
            where T8 : class, IEntityAspect
            => new(_registry);
    }
}
