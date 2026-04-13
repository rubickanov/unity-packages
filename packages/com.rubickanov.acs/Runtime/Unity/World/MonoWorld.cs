using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Scene-driven Unity adapter for the pure-C# <see cref="Runtime.World"/>. Owns a single
    /// embedded <c>World</c> instance, assigns it as <see cref="Runtime.World.Current"/> during
    /// <c>Awake</c>, and clears it during <c>OnDestroy</c>. All <see cref="IEntity"/> calls on
    /// the MonoWorld delegate into the embedded world so world-scoped aspects live in one
    /// place — the Mono container never stores its own copies.
    /// <para/>
    /// Execution order is forced ahead of user components so <see cref="Runtime.World.Current"/>
    /// is non-null by the time other <see cref="MonoEntity"/> children run <c>Awake</c>.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class MonoWorld : SingletonMonoEntity<MonoWorld>
    {
        private readonly World _world = new();

        /// <summary>
        /// The embedded pure-C# world. Exposed so tests and tooling can inspect the registry
        /// directly (<c>MonoWorld.Instance.World.Registry</c>) or invoke instance-level APIs
        /// without going through the static <see cref="Runtime.World.Current"/> slot.
        /// </summary>
        public World World => _world;

        // Reset the pure World.Current slot at the start of every play session. Mirrors
        // MonoEntity.ResetStaticEvents — if a prior Play Mode exited with an exception and
        // Domain Reload is disabled in Project Settings, the static pointer would otherwise
        // survive into the next session and the first SetCurrent call would throw.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayStart() => World.ForceResetCurrent();

        protected override void Awake()
        {
            base.Awake();
            // SingletonMonoEntity.Awake destroys the duplicate GameObject before returning.
            // For the surviving instance, Instance == this is true here; for a duplicate it
            // is not — and we must not touch World.Current from the loser.
            if (Instance == this)
            {
                World.SetCurrent(_world);
                _world.AspectCreated += ForwardAspectCreated;
            }
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                _world.AspectCreated -= ForwardAspectCreated;
                World.ClearCurrent(_world);
            }

            // Dispose unconditionally — a duplicate MonoWorld still ran its field-initializer
            // and owns a World instance that nothing else will clean up. Skipping this for
            // duplicates would leak the World (silently for now, loudly once World grows
            // external references).
            _world.Dispose();

            base.OnDestroy();
        }

        private static void ForwardAspectCreated(IEntity entity, Type aspectType)
            => InvokeOnAspectCreated(entity, aspectType);

        // _world.Require<T>() is unreachable on the concrete World type — the public method-name
        // is taken by the static shortcut. Go through the IEntity interface to hit the explicit
        // implementation. Same reasoning applies to TryGet/Has/GetAllAspects/AspectTypes for
        // symmetry, even though those don't have the static collision today.
        public override T Require<T>() => ((IEntity)_world).Require<T>();

        public override bool TryGet<T>([NotNullWhen(returnValue: true)] out T? aspect) where T : class
            => _world.TryGet(out aspect);

        public override bool Has<T>() => _world.Has<T>();

        public override IEnumerable<object> GetAllAspects() => _world.GetAllAspects();

        public override Dictionary<Type, object>.KeyCollection AspectTypes => _world.AspectTypes;
    }
}
