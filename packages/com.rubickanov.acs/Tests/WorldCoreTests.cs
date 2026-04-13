using System.Linq;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Edit-mode tests that exercise <see cref="WorldCore"/> directly against
    /// pure POCO <see cref="Entity"/> instances — no <c>GameObject</c>, no
    /// <see cref="World"/> singleton. Serves as a proof-of-concept for headless
    /// simulations that want to reuse the ACS query surface without Unity.
    /// </summary>
    [TestFixture]
    public class WorldCoreTests
    {
        [Test]
        public void Register_SameEntityTwice_DoesNotDuplicate()
        {
            var core = new WorldCore();
            var entity = new Entity();

            core.Register(entity, typeof(TestAspectA));
            core.Register(entity, typeof(TestAspectA));

            Assert.AreEqual(1, core.Registry.GetAllWith(typeof(TestAspectA)).Count);
        }

        [Test]
        public void Query_SingleAspect_YieldsRegisteredInstances()
        {
            var core = new WorldCore();
            var a = new Entity();
            var b = new Entity();
            var aspectA = a.Require<TestAspectA>();
            var aspectB = b.Require<TestAspectA>();
            core.Register(a, typeof(TestAspectA));
            core.Register(b, typeof(TestAspectA));

            var results = core.Query<TestAspectA>().ToList();

            CollectionAssert.AreEquivalent(new[] { aspectA, aspectB }, results);
        }

        [Test]
        public void Query_TwoAspects_YieldsTuplesForEntitiesCarryingBoth()
        {
            var core = new WorldCore();
            var both = new Entity();
            both.Require<TestAspectA>();
            both.Require<TestAspectB>();
            core.Register(both, typeof(TestAspectA));
            core.Register(both, typeof(TestAspectB));

            var onlyA = new Entity();
            onlyA.Require<TestAspectA>();
            core.Register(onlyA, typeof(TestAspectA));

            var tuples = core.Query<TestAspectA, TestAspectB>().ToList();

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
            // WorldCore does not auto-unregister on Entity.Dispose today —
            // subscribers do that via Destroyed. This test just pins the
            // contract: Unregister makes the entity disappear from queries.
            var core = new WorldCore();
            var entity = new Entity();
            entity.Require<TestAspectA>();
            core.Register(entity, typeof(TestAspectA));

            entity.Destroyed += e => core.Unregister(e, e.AspectTypes);
            entity.Dispose();

            Assert.IsEmpty(core.Query<TestAspectA>().ToList());
        }

        [Test]
        public void Query_OnEmptyCore_ReturnsEmpty()
        {
            var core = new WorldCore();

            Assert.IsEmpty(core.Query<TestAspectA>().ToList());
            Assert.IsEmpty(core.Query<TestAspectA, TestAspectB>().ToList());
        }

        [Test]
        public void Clear_DropsAllRegistrations()
        {
            var core = new WorldCore();
            var entity = new Entity();
            core.Register(entity, typeof(TestAspectA));
            core.Register(entity, typeof(TestAspectB));

            core.Clear();

            Assert.IsEmpty(core.Query<TestAspectA>().ToList());
            Assert.IsEmpty(core.Query<TestAspectB>().ToList());
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
