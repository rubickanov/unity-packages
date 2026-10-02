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
        public void Dispose_ThenSubscribe_HandlerNeverFires()
        {
            var entity = new Entity();
            entity.Dispose();

            int fireCount = 0;
            Assert.DoesNotThrow(() => entity.Destroyed += _ => fireCount++,
                "Subscribing after Dispose must be legal — null += handler is valid C# and callers should not crash.");

            Assert.AreEqual(0, fireCount,
                "Post-Dispose subscribers must be silently inert — Destroyed will never fire again.");
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

        [Test]
        public void CtorWithWorld_AfterConstruction_IsFindableByIdBeforeAnyRequire()
        {
            var world = new World();
            var entity = new Entity(world);

            var found = world.TryFindById(entity.Id, out var resolved);

            Assert.IsTrue(found,
                "Entity(World) must register itself in the by-id index at construction — before any Require<T> call. " +
                "This is the core invariant that makes EntityId useful as a cross-reference key in aspect data.");
            Assert.AreSame(entity, resolved);
        }

        [Test]
        public void CtorWithoutWorld_IsNotFindableViaAnyWorld()
        {
            var world = new World();
            var entity = new Entity();

            Assert.IsFalse(world.TryFindById(entity.Id, out _),
                "Parameterless Entity() opts out of world integration — it must not leak into any world's by-id index.");
        }

        [Test]
        public void Dispose_AfterCall_IsNotFindableById()
        {
            var world = new World();
            var entity = new Entity(world);
            var id = entity.Id;

            entity.Dispose();

            Assert.IsFalse(world.TryFindById(id, out _),
                "Dispose must drop the by-id entry — otherwise a disposed entity stays addressable forever.");
        }

        [Test]
        public void Dispose_DestroyedSubscriber_CanStillFindEntityById()
        {
            // Pins the register/unregister ordering: the by-id slot must outlive the Destroyed
            // event so subscribers can use TryFindById on the dying entity itself (useful for
            // e.g. a cache key lookup) before the id disappears from the world.
            var world = new World();
            var entity = new Entity(world);

            bool foundDuringDestroyed = false;
            entity.Destroyed += e =>
            {
                foundDuringDestroyed = world.TryFindById(e.Id, out var resolved) && ReferenceEquals(resolved, e);
            };

            entity.Dispose();

            Assert.IsTrue(foundDuringDestroyed,
                "Destroyed must fire while the by-id slot is still live — unregister happens after subscribers run.");
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
