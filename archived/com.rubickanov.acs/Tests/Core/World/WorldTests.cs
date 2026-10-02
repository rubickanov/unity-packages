using System;
using System.Linq;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Edit-mode tests that exercise the pure <see cref="World"/> directly against
    /// POCO <see cref="Entity"/> instances — no <c>GameObject</c>, no
    /// <see cref="MonoWorld"/> singleton. Serves as a proof-of-concept for headless
    /// simulations that want to reuse the ACS query surface without Unity.
    /// </summary>
    [TestFixture]
    public class WorldTests
    {
        [Test]
        public void Register_SameEntityTwice_DoesNotDuplicate()
        {
            var world = new World();
            var entity = new Entity();

            world.Register(entity, typeof(TestAspectA));
            world.Register(entity, typeof(TestAspectA));

            Assert.AreEqual(1, world.Registry.GetAllWith(typeof(TestAspectA)).Count);
        }

        [Test]
        public void Query_SingleAspect_YieldsRegisteredInstances()
        {
            var world = new World();
            var a = new Entity();
            var b = new Entity();
            var aspectA = a.Require<TestAspectA>();
            var aspectB = b.Require<TestAspectA>();
            world.Register(a, typeof(TestAspectA));
            world.Register(b, typeof(TestAspectA));

            var results = world.QueryLocal<TestAspectA>().ToList();

            CollectionAssert.AreEquivalent(new[] { aspectA, aspectB }, results);
        }

        [Test]
        public void Query_TwoAspects_YieldsTuplesForEntitiesCarryingBoth()
        {
            var world = new World();
            var both = new Entity();
            both.Require<TestAspectA>();
            both.Require<TestAspectB>();
            world.Register(both, typeof(TestAspectA));
            world.Register(both, typeof(TestAspectB));

            var onlyA = new Entity();
            onlyA.Require<TestAspectA>();
            world.Register(onlyA, typeof(TestAspectA));

            var tuples = world.QueryLocal<TestAspectA, TestAspectB>().ToList();

            Assert.AreEqual(1, tuples.Count);
            Assert.AreSame(both, tuples[0].Entity);
            // Tuple.Entity is IEntity — the pure Entity POCO appears here without
            // any Unity involvement. If this field's static type ever drifts back
            // to MonoEntity, pocket entities cannot participate in queries.
            Assert.IsInstanceOf<Entity>(tuples[0].Entity);
        }

        [Test]
        public void Query_AfterDispose_DoesNotYieldReleasedEntity()
        {
            // World does not auto-unregister on Entity.Dispose today —
            // subscribers do that via Destroyed. This test just pins the
            // contract: Unregister makes the entity disappear from queries.
            var world = new World();
            var entity = new Entity();
            entity.Require<TestAspectA>();
            world.Register(entity, typeof(TestAspectA));

            entity.Destroyed += e => world.Unregister(e, e.AspectTypes);
            entity.Dispose();

            Assert.IsEmpty(world.QueryLocal<TestAspectA>().ToList());
        }

        [Test]
        public void Query_OnEmptyWorld_ReturnsEmpty()
        {
            var world = new World();

            Assert.IsEmpty(world.QueryLocal<TestAspectA>().ToList());
            Assert.IsEmpty(world.QueryLocal<TestAspectA, TestAspectB>().ToList());
        }

        [Test]
        public void Clear_DropsAllRegistrations()
        {
            var world = new World();
            var entity = new Entity();
            world.Register(entity, typeof(TestAspectA));
            world.Register(entity, typeof(TestAspectB));

            world.Clear();

            Assert.IsEmpty(world.QueryLocal<TestAspectA>().ToList());
            Assert.IsEmpty(world.QueryLocal<TestAspectB>().ToList());
        }

        [Test]
        public void Ctor_Always_RegistersSelfInByIdIndex()
        {
            var world = new World();

            var found = world.TryFindById(world.Id, out var resolved);

            Assert.IsTrue(found,
                "World implements IEntity, so TryFindById(world.Id) must resolve to the world itself. " +
                "Without self-registration the 'World is an IEntity' invariant would leak.");
            Assert.AreSame(world, resolved);
        }

        [Test]
        public void TryFindById_NoneId_ReturnsFalse()
        {
            var world = new World();

            var found = world.TryFindById(EntityId.None, out var resolved);

            Assert.IsFalse(found);
            Assert.IsNull(resolved);
        }

        [Test]
        public void TryFindById_UnknownId_ReturnsFalse()
        {
            var world = new World();
            // An arbitrary value that could never have been allocated yet (future counter position
            // would have to wrap past ulong.MaxValue first).
            var unknown = new EntityId(ulong.MaxValue);

            var found = world.TryFindById(unknown, out var resolved);

            Assert.IsFalse(found);
            Assert.IsNull(resolved);
        }

        [Test]
        public void Dispose_AfterCall_ClearsByIdIndex()
        {
            var world = new World();
            var entity = new Entity(world);

            world.Dispose();

            Assert.IsFalse(world.TryFindById(entity.Id, out _),
                "World.Dispose must clear the by-id index — otherwise a torn-down world keeps references to its entities alive.");
            Assert.IsFalse(world.TryFindById(world.Id, out _),
                "The world's own self-registration must also be cleared.");
        }

        [Test]
        public void Dispose_WhileCurrent_ClearsCurrent()
        {
            // A world disposed while still Current would leave the static slot pointing at a
            // dead instance — the next World.Require/Query would silently operate on an empty
            // registry with no signal that setup is broken. Dispose must drop the slot.
            var world = new World();
            World.SetCurrent(world);

            try
            {
                world.Dispose();

                Assert.IsNull(World.Current,
                    "Disposing the Current world must null the static slot so callers don't " +
                    "silently operate on a dead instance.");
            }
            finally
            {
                // Defensive — if the assertion failed, make sure the next test doesn't inherit state.
                typeof(World)
                    .GetMethod("ForceResetCurrent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, null);
            }
        }

        [Test]
        public void Dispose_WhenDifferentWorldIsCurrent_DoesNotTouchCurrent()
        {
            // Only clear Current if it points at the world being disposed — disposing a pocket
            // world must not kick out the main world's assignment.
            var pocket = new World();
            var main = new World();
            World.SetCurrent(main);

            try
            {
                pocket.Dispose();

                Assert.AreSame(main, World.Current,
                    "Disposing a non-Current world must leave the Current slot untouched.");
            }
            finally
            {
                typeof(World)
                    .GetMethod("ForceResetCurrent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, null);
            }
        }

        [Test]
        public void Register_AfterDispose_ThrowsObjectDisposedException()
        {
            var world = new World();
            var entity = new Entity();
            world.Dispose();

            Assert.Throws<ObjectDisposedException>(() => world.Register(entity, typeof(TestAspectA)));
            Assert.Throws<ObjectDisposedException>(() => world.Register(entity));
        }

        [Test]
        public void Require_AfterDispose_ThrowsObjectDisposedException()
        {
            // IEntity.Require on a disposed World must throw so an Entity cached from before
            // Dispose can't silently create orphaned aspects after its world is gone. Guard
            // surfaces "entity outlives world" bugs at the call site.
            var world = new World();
            world.Dispose();

            Assert.Throws<ObjectDisposedException>(() => ((IEntity)world).Require<TestAspectA>());
        }

        [Test]
        public void SetCurrent_FromNull_FiresCurrentChangedWithNewWorld()
        {
            // CurrentChanged is the hook MonoEntity relies on for retroactive registration —
            // entities that Awoke without a world must be notified when one becomes current.
            var world = new World();
            World fired = null;
            Action<World> handler = w => fired = w;
            World.CurrentChanged += handler;

            try
            {
                World.SetCurrent(world);

                Assert.AreSame(world, fired,
                    "CurrentChanged must fire with the just-assigned world when Current transitions from null.");
            }
            finally
            {
                World.CurrentChanged -= handler;
                ResetWorldStatics();
            }
        }

        [Test]
        public void SetCurrent_SameWorldTwice_FiresCurrentChangedOnlyOnce()
        {
            // Idempotent reassignment must not re-fire CurrentChanged — otherwise MonoEntity
            // would attempt to register twice (RegisterById would throw on the duplicate id
            // collision path, or the per-aspect Register would re-invoke AspectCreated and
            // confuse subscribers like acs.netcode that dedupe on first-seen).
            var world = new World();
            var fireCount = 0;
            Action<World> handler = _ => fireCount++;
            World.CurrentChanged += handler;

            try
            {
                World.SetCurrent(world);
                World.SetCurrent(world);

                Assert.AreEqual(1, fireCount,
                    "SetCurrent with the already-Current world is a no-op and must not re-raise CurrentChanged.");
            }
            finally
            {
                World.CurrentChanged -= handler;
                ResetWorldStatics();
            }
        }

        [Test]
        public void ClearCurrent_DoesNotFireCurrentChanged()
        {
            // CurrentChanged is scoped to null→world transitions — the world-→null teardown
            // has no subscriber contract to honor (a disposed MonoEntity doesn't need to know).
            // Keep the event's semantics minimal so future consumers can assume "fired = world available".
            var world = new World();
            World.SetCurrent(world);
            var fireCount = 0;
            Action<World> handler = _ => fireCount++;
            World.CurrentChanged += handler;

            try
            {
                World.ClearCurrent(world);

                Assert.AreEqual(0, fireCount,
                    "ClearCurrent must not fire CurrentChanged — the event signals world-becomes-available, not world-goes-away.");
            }
            finally
            {
                World.CurrentChanged -= handler;
                ResetWorldStatics();
            }
        }

        private static void ResetWorldStatics()
        {
            typeof(World)
                .GetMethod("ForceResetCurrent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null);
            typeof(World)
                .GetMethod("ResetStaticEvents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null);
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
