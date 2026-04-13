using System.Linq;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// End-to-end proof that ACS can run without Unity. Each test wires the
    /// full stack — <see cref="Entity"/>, <see cref="WorldCore"/>,
    /// <see cref="IEntityLogic"/>, <see cref="EntityExtensions.AttachLogic"/>,
    /// <see cref="ITickable"/> — on plain C# types with zero
    /// <c>GameObject.AddComponent</c> calls. If any of these tests starts
    /// needing Unity, the pure-core contract has regressed and the L2
    /// headless-simulation path is broken.
    /// </summary>
    [TestFixture]
    public class PureCoreIntegrationTests
    {
        private sealed class HealthAspect : IEntityAspect
        {
            public readonly ReactiveProperty<int> Current = new(100);
        }

        private sealed class PositionAspect : IEntityAspect
        {
            public readonly ReactiveProperty<float> X = new(0f);
        }

        /// <summary>
        /// Pure reactive logic: subscribes to an aspect event, releases the
        /// subscription on Dispose. The exact 80%-case the pure-core layer is
        /// meant to serve.
        /// </summary>
        private sealed class DeathWatchLogic : IEntityLogic
        {
            public int DeathCount;
            private readonly System.IDisposable _sub;

            public DeathWatchLogic(HealthAspect health)
            {
                _sub = health.Current.Subscribe(v => { if (v <= 0) DeathCount++; });
            }

            public void Dispose() => _sub.Dispose();
        }

        /// <summary>
        /// Tickable: writes to a replicated-style aspect every step. In a
        /// headless simulation this is what an ISimulate<TInput> implementation
        /// looks like once you strip the NGO scaffolding.
        /// </summary>
        private sealed class ConstantMoveTickable : ITickable
        {
            private readonly PositionAspect _pos;
            private readonly float _speed;

            public ConstantMoveTickable(PositionAspect pos, float speed)
            {
                _pos = pos;
                _speed = speed;
            }

            public void Tick(float dt) => _pos.X.Value += _speed * dt;
        }

        [Test]
        public void PureStack_WiresAspectLogicAndQueries_WithoutUnity()
        {
            // One shared registry, two pure entities, one logic each, one query.
            // No Unity, no DI — a console host would look exactly like this.
            var core = new WorldCore();

            var hero = new Entity();
            var hpHero = hero.Require<HealthAspect>();
            hero.Require<PositionAspect>();
            core.Register(hero, typeof(HealthAspect));
            core.Register(hero, typeof(PositionAspect));
            var heroWatch = hero.AttachLogic(new DeathWatchLogic(hpHero));

            var mob = new Entity();
            var hpMob = mob.Require<HealthAspect>();
            core.Register(mob, typeof(HealthAspect));
            var mobWatch = mob.AttachLogic(new DeathWatchLogic(hpMob));

            // World.Query-equivalent — via WorldCore, no singleton in sight.
            var withHealth = core.Query<HealthAspect>().ToList();
            CollectionAssert.AreEquivalent(new[] { hpHero, hpMob }, withHealth);

            // Multi-aspect query picks out hero only (mob has no PositionAspect).
            var tuples = core.Query<HealthAspect, PositionAspect>().ToList();
            Assert.AreEqual(1, tuples.Count);
            Assert.AreSame(hero, tuples[0].Entity);

            // Logic reacts to aspect mutation.
            hpHero.Current.Value = 0;
            Assert.AreEqual(1, heroWatch.DeathCount);
            Assert.AreEqual(0, mobWatch.DeathCount);

            // Destruction releases logic via Destroyed → AttachLogic's hook.
            hero.Destroyed += e => core.Unregister(e, e.AspectTypes);
            hero.Dispose();
            Assert.IsFalse(core.Query<PositionAspect>().Any(),
                "hero was the only PositionAspect carrier — must be unregistered after Dispose.");

            // Double-drive the watch: if we accidentally kept a dangling
            // subscription past Dispose, further writes would increment.
            hpHero.Current.Value = -999;
            Assert.AreEqual(1, heroWatch.DeathCount,
                "DeathWatchLogic must have been disposed — further Current writes shouldn't reach it.");
        }

        [Test]
        public void PureStack_TickableDrivenByExternalLoop_MutatesAspect()
        {
            // The L2 headless case: no MonoBehaviour driver, just a loop in the
            // caller. This is what ACS.Simulate will look like — same ITickable,
            // different frame source.
            var entity = new Entity();
            var pos = entity.Require<PositionAspect>();
            var tickable = new ConstantMoveTickable(pos, speed: 2f);

            // External fixed-step loop — 30 ticks at 1/30 s each = 1 s of sim.
            const float dt = 1f / 30f;
            for (int i = 0; i < 30; i++)
                tickable.Tick(dt);

            // Integrate: speed * totalTime = 2 * 1 = 2.
            Assert.AreEqual(2f, pos.X.Value, 1e-4f,
                "ITickable should be drivable from a plain loop — if this fails the implementation is leaking a Unity dependency.");
        }
    }
}
