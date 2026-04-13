using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

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

        private MonoEntity NewEntity()
        {
            var go = new GameObject(nameof(EntityRegistryTests));
            _spawned.Add(go);
            return go.AddComponent<MonoEntity>();
        }

        private static Dictionary<Type, object>.KeyCollection TypeKeys(params Type[] types)
        {
            var dict = new Dictionary<Type, object>(types.Length);
            foreach (var t in types)
                dict[t] = null!;
            return dict.Keys;
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
        public void Unregister_RemovesEntityFromProvidedBuckets()
        {
            var entity = NewEntity();
            _registry.Register(entity, typeof(TestAspectA));
            _registry.Register(entity, typeof(TestAspectB));

            _registry.Unregister(entity, TypeKeys(typeof(TestAspectA), typeof(TestAspectB)));

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

            _registry.Unregister(first, TypeKeys(typeof(TestAspectA)));

            CollectionAssert.AreEquivalent(new[] { second }, _registry.GetAllWith(typeof(TestAspectA)));
        }

        [Test]
        public void Unregister_UnknownEntity_DoesNotThrow()
        {
            var entity = NewEntity();

            Assert.DoesNotThrow(() => _registry.Unregister(entity, TypeKeys(typeof(TestAspectA))));
        }

        [Test]
        public void Unregister_OnlyTouchesBucketsForProvidedTypes_LeavesOthersIntact()
        {
            var entity = NewEntity();
            var bystander = NewEntity();
            _registry.Register(entity, typeof(TestAspectA));
            _registry.Register(entity, typeof(TestAspectB));
            _registry.Register(bystander, typeof(TestAspectC));

            _registry.Unregister(entity, TypeKeys(typeof(TestAspectA)));

            Assert.IsEmpty(_registry.GetAllWith(typeof(TestAspectA)));
            CollectionAssert.AreEquivalent(new[] { entity }, _registry.GetAllWith(typeof(TestAspectB)));
            CollectionAssert.AreEquivalent(new[] { bystander }, _registry.GetAllWith(typeof(TestAspectC)));
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
        private class TestAspectC : IEntityAspect { }
    }
}
