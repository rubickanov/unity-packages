using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Edit-mode tests for the pure <see cref="Entity"/> POCO. No Unity types,
    /// no <c>GameObject</c> — if these tests require anything from the player
    /// loop, the pure-core contract has been broken.
    /// </summary>
    [TestFixture]
    public class EntityTests
    {
        [Test]
        public void Require_SameTypeTwice_ReturnsSameInstance()
        {
            var entity = new Entity();

            var first = entity.Require<TestAspectA>();
            var second = entity.Require<TestAspectA>();

            Assert.AreSame(first, second);
        }

        [Test]
        public void Require_DifferentTypes_ReturnsDifferentInstances()
        {
            var entity = new Entity();

            var a = entity.Require<TestAspectA>();
            var b = entity.Require<TestAspectB>();

            Assert.IsInstanceOf<TestAspectA>(a);
            Assert.IsInstanceOf<TestAspectB>(b);
            Assert.AreNotSame(a, b);
        }

        [Test]
        public void TryGet_BeforeRequire_ReturnsFalse()
        {
            var entity = new Entity();

            var result = entity.TryGet<TestAspectA>(out var aspect);

            Assert.IsFalse(result);
            Assert.IsNull(aspect);
        }

        [Test]
        public void TryGet_AfterRequire_ReturnsTrueAndInstance()
        {
            var entity = new Entity();
            var created = entity.Require<TestAspectA>();

            var result = entity.TryGet<TestAspectA>(out var aspect);

            Assert.IsTrue(result);
            Assert.AreSame(created, aspect);
        }

        [Test]
        public void Has_ReflectsRequire()
        {
            var entity = new Entity();

            Assert.IsFalse(entity.Has<TestAspectA>());
            entity.Require<TestAspectA>();
            Assert.IsTrue(entity.Has<TestAspectA>());
        }

        [Test]
        public void GetAllAspects_ReturnsEveryRequiredInstance()
        {
            var entity = new Entity();
            var a = entity.Require<TestAspectA>();
            var b = entity.Require<TestAspectB>();

            CollectionAssert.AreEquivalent(new object[] { a, b }, entity.GetAllAspects());
        }

        [Test]
        public void Dispose_FiresDestroyedOnce()
        {
            var entity = new Entity();
            int fireCount = 0;
            IEntity captured = null;
            entity.Destroyed += e => { fireCount++; captured = e; };

            entity.Dispose();
            entity.Dispose();

            Assert.AreEqual(1, fireCount, "Destroyed must fire exactly once even across repeated Dispose calls.");
            Assert.AreSame(entity, captured);
        }

        [Test]
        public void Dispose_ClearsAspectDictionary()
        {
            var entity = new Entity();
            entity.Require<TestAspectA>();
            entity.Require<TestAspectB>();

            entity.Dispose();

            Assert.IsEmpty(entity.GetAllAspects(),
                "Dispose must release aspect references so the entity is not a lingering GC root for replicated state buffers.");
            Assert.IsFalse(entity.Has<TestAspectA>());
            Assert.IsFalse(entity.Has<TestAspectB>());
        }

        [Test]
        public void Dispose_WithNoSubscribers_DoesNotThrow()
        {
            var entity = new Entity();
            entity.Require<TestAspectA>();

            Assert.DoesNotThrow(() => entity.Dispose());
        }

        [Test]
        public void Destroyed_SubscriberCanObserveEntityDuringFire()
        {
            var entity = new Entity();
            entity.Require<TestAspectA>();

            bool sawAspect = false;
            entity.Destroyed += e =>
            {
                // During Destroyed the aspect dictionary must still be intact so
                // subscribers can e.g. unwire replication bindings before the
                // entity's data disappears.
                sawAspect = e.Has<TestAspectA>();
            };

            entity.Dispose();

            Assert.IsTrue(sawAspect,
                "Aspects must still be readable during Destroyed — cleanup happens after subscribers run.");
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
