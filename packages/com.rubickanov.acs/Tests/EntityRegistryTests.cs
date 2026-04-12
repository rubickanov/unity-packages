using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class EntityRegistryTests
    {
        private readonly List<GameObject> _spawned = new();
        private EntityRegistry _registry = default!;

        [SetUp]
        public void SetUp()
        {
            _registry = new EntityRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private EntityContext NewEntity()
        {
            var go = new GameObject(nameof(EntityRegistryTests));
            _spawned.Add(go);
            return go.AddComponent<EntityContext>();
        }

        [Test]
        public void GetAllWith_UnknownType_ReturnsEmpty()
        {
            var result = _registry.GetAllWith(typeof(TestAspectA));

            Assert.IsNotNull(result);
            Assert.IsEmpty(result);
        }

        [Test]
        public void Register_SingleEntity_AppearsInBucket()
        {
            var entity = NewEntity();

            _registry.Register(entity, typeof(TestAspectA));

            CollectionAssert.AreEquivalent(new[] { entity }, _registry.GetAllWith(typeof(TestAspectA)));
        }

        [Test]
        public void Register_SameEntityTwice_BucketContainsOneCopy()
        {
            var entity = NewEntity();

            _registry.Register(entity, typeof(TestAspectA));
            _registry.Register(entity, typeof(TestAspectA));

            Assert.AreEqual(1, _registry.GetAllWith(typeof(TestAspectA)).Count);
        }

        [Test]
        public void Register_DifferentTypes_BucketsAreIndependent()
        {
            var entity = NewEntity();

            _registry.Register(entity, typeof(TestAspectA));
            _registry.Register(entity, typeof(TestAspectB));

            Assert.AreEqual(1, _registry.GetAllWith(typeof(TestAspectA)).Count);
            Assert.AreEqual(1, _registry.GetAllWith(typeof(TestAspectB)).Count);
        }

        [Test]
        public void Unregister_RemovesEntityFromAllBuckets()
        {
            var entity = NewEntity();
            _registry.Register(entity, typeof(TestAspectA));
            _registry.Register(entity, typeof(TestAspectB));

            _registry.Unregister(entity);

            Assert.IsEmpty(_registry.GetAllWith(typeof(TestAspectA)));
            Assert.IsEmpty(_registry.GetAllWith(typeof(TestAspectB)));
        }

        [Test]
        public void Unregister_LeavesOtherEntities()
        {
            var first = NewEntity();
            var second = NewEntity();
            _registry.Register(first, typeof(TestAspectA));
            _registry.Register(second, typeof(TestAspectA));

            _registry.Unregister(first);

            CollectionAssert.AreEquivalent(new[] { second }, _registry.GetAllWith(typeof(TestAspectA)));
        }

        [Test]
        public void Unregister_UnknownEntity_DoesNotThrow()
        {
            var entity = NewEntity();

            Assert.DoesNotThrow(() => _registry.Unregister(entity));
        }

        [Test]
        public void Clear_EmptiesAllBuckets()
        {
            var entity = NewEntity();
            _registry.Register(entity, typeof(TestAspectA));
            _registry.Register(entity, typeof(TestAspectB));

            _registry.Clear();

            Assert.IsEmpty(_registry.GetAllWith(typeof(TestAspectA)));
            Assert.IsEmpty(_registry.GetAllWith(typeof(TestAspectB)));
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
