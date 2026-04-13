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

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
