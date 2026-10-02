using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;
using EntityId = Rubickanov.ACS.Runtime.EntityId;
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

        [Test]
        public void RegisterById_NewEntry_IsFindable()
        {
            var entity = new FakeEntity(new EntityId(1));

            _registry.RegisterById(entity);

            Assert.IsTrue(_registry.TryFindById(entity.Id, out var resolved));
            Assert.AreSame(entity, resolved);
        }

        [Test]
        public void RegisterById_SameEntityTwice_IsNoOp()
        {
            var entity = new FakeEntity(new EntityId(1));
            _registry.RegisterById(entity);

            Assert.DoesNotThrow(() => _registry.RegisterById(entity),
                "Re-registering the same reference under the same id must be idempotent — not every caller can cheaply track whether they've already registered.");

            Assert.IsTrue(_registry.TryFindById(entity.Id, out var resolved));
            Assert.AreSame(entity, resolved);
        }

        [Test]
        public void RegisterById_DifferentEntitySameId_Throws()
        {
            var first = new FakeEntity(new EntityId(42));
            var second = new FakeEntity(new EntityId(42));
            _registry.RegisterById(first);

            Assert.Throws<InvalidOperationException>(() => _registry.RegisterById(second),
                "A second entity colliding on an already-registered id must fail loudly — silently overwriting the slot causes 'entity vanished from query' bugs that are expensive to diagnose.");

            Assert.IsTrue(_registry.TryFindById(new EntityId(42), out var resolved));
            Assert.AreSame(first, resolved,
                "After the throw the original slot must remain intact — the half-registered state of the collision attempt should not leak.");
        }

        [Test]
        public void UnregisterById_AfterRegister_RemovesEntry()
        {
            var entity = new FakeEntity(new EntityId(1));
            _registry.RegisterById(entity);

            _registry.UnregisterById(entity);

            Assert.IsFalse(_registry.TryFindById(entity.Id, out _));
        }

        [Test]
        public void UnregisterById_WhenSlotHoldsDifferentEntity_DoesNotRemove()
        {
            // Model the "stale Unregister issued by a dead previous owner after the slot has been
            // reclaimed" scenario. The slot must be left intact even though both entities share the id.
            var current = new FakeEntity(new EntityId(7));
            var stale = new FakeEntity(new EntityId(7));
            _registry.RegisterById(current);

            _registry.UnregisterById(stale);

            Assert.IsTrue(_registry.TryFindById(new EntityId(7), out var resolved));
            Assert.AreSame(current, resolved);
        }

        [Test]
        public void UnregisterById_UnknownEntity_DoesNotThrow()
        {
            var entity = new FakeEntity(new EntityId(1));

            Assert.DoesNotThrow(() => _registry.UnregisterById(entity));
        }

        [Test]
        public void TryFindById_None_ReturnsFalseWithoutTouchingIndex()
        {
            // Register a real entry; verify None lookup is rejected even though the registry is populated.
            _registry.RegisterById(new FakeEntity(new EntityId(1)));

            Assert.IsFalse(_registry.TryFindById(EntityId.None, out var resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void Clear_AlsoEmptiesByIdIndex()
        {
            var entity = new FakeEntity(new EntityId(1));
            _registry.RegisterById(entity);

            _registry.Clear();

            Assert.IsFalse(_registry.TryFindById(entity.Id, out _),
                "Clear must wipe both the per-aspect index and the by-id index — otherwise a torn-down world leaks references.");
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
        private class TestAspectC : IEntityAspect { }

        // Minimal IEntity double for id-index tests. Lets us fabricate two distinct references
        // sharing an EntityId value — something the normal Entity/MonoEntity constructors never
        // allow because they source ids from a monotonic counter.
        private sealed class FakeEntity : IEntity
        {
            public EntityId Id { get; }
            public event Action<IEntity>? Destroyed { add { } remove { } }

            public FakeEntity(EntityId id) { Id = id; }

            public T Require<T>() where T : class, IEntityAspect, new() => throw new NotSupportedException();
            public bool TryGet<T>(out T? aspect) where T : class, IEntityAspect { aspect = null; return false; }
            public bool Has<T>() where T : class, IEntityAspect => false;
            public IEnumerable<object> GetAllAspects() => Array.Empty<object>();
            public Dictionary<Type, object>.KeyCollection AspectTypes => new Dictionary<Type, object>().Keys;
        }
    }
}
