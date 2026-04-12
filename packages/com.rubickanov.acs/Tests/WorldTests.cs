using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class WorldTests
    {
        // Unity does not fire Awake/OnDestroy on components in EditMode tests, so we invoke them
        // via reflection. SendMessage would also work but trips Unity's ShouldRunBehaviour gate
        // and produces log-assertion noise the test framework then reports as a failure.
        private static readonly MethodInfo WorldAwakeMethod = typeof(World).GetMethod(
            "Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!;
        private static readonly MethodInfo EntityOnDestroyMethod = typeof(EntityContext).GetMethod(
            "OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!;
        private static readonly PropertyInfo WorldInstanceProp = typeof(SingletonEntityContext<World>)
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!;

        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            // A previous test may leave Instance holding a Unity-destroyed reference that passes
            // the overloaded `!= null` check but is not a true null. Force-clear the backing
            // property so IsNull-style preconditions behave like runtime would.
            ResetWorldInstance();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                DestroyWithLifecycle(go);
            _spawned.Clear();
            ResetWorldInstance();
        }

        private World NewWorld(string name = "World")
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            var world = go.AddComponent<World>();
            WorldAwakeMethod.Invoke(world, null);
            return world;
        }

        private EntityContext NewEntity(string name = "Entity")
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<EntityContext>();
        }

        // Destroys the GameObject the way Unity would at runtime: invoke OnDestroy on every
        // EntityContext on it (so World.Unregister and Instance-clear fire) before the actual
        // DestroyImmediate call. Safe to pass already-destroyed or null references.
        private static void DestroyWithLifecycle(GameObject go)
        {
            if (go == null)
                return;
            foreach (var ec in go.GetComponents<EntityContext>())
                EntityOnDestroyMethod.Invoke(ec, null);
            Object.DestroyImmediate(go);
        }

        private static void ResetWorldInstance() => WorldInstanceProp.SetValue(null, null);

        [Test]
        public void Awake_FirstInstance_SetsStaticReference()
        {
            var world = NewWorld();

            Assert.AreSame(world, World.Instance);
        }

        [Test]
        public void Awake_DuplicateInstance_LeavesOriginalAsInstance()
        {
            var first = NewWorld("FirstWorld");

            // The duplicate's Awake calls Destroy(gameObject); in EditMode Unity downgrades
            // that to a warning. We don't care that the GO survives — we care that Instance
            // still points at the first world.
            LogAssert.ignoreFailingMessages = true;
            NewWorld("SecondWorld");
            LogAssert.ignoreFailingMessages = false;

            Assert.AreSame(first, World.Instance);
        }

        [Test]
        public void OnDestroy_ClearsStaticReference()
        {
            var world = NewWorld();
            var go = world.gameObject;

            DestroyWithLifecycle(go);
            _spawned.Remove(go);

            Assert.IsNull(World.Instance);
        }

        [Test]
        public void Require_OnEntityWithWorldPresent_AutoRegistersWithIndex()
        {
            NewWorld();
            var entity = NewEntity();

            entity.Require<TestAspectA>();

            CollectionAssert.Contains(World.Instance!.Registry.GetAllWith(typeof(TestAspectA)), entity);
        }

        [Test]
        public void QuerySingle_YieldsAspectFromEveryEntity()
        {
            NewWorld();
            var a = NewEntity("A").Require<TestAspectA>();
            var b = NewEntity("B").Require<TestAspectA>();

            var all = World.Query<TestAspectA>().ToList();

            CollectionAssert.AreEquivalent(new[] { a, b }, all);
        }

        [Test]
        public void QuerySingle_SkipsEntitiesWithoutAspect()
        {
            NewWorld();
            var withA = NewEntity("HasA");
            withA.Require<TestAspectA>();
            var withB = NewEntity("HasB");
            withB.Require<TestAspectB>();

            var results = World.Query<TestAspectA>().ToList();

            Assert.AreEqual(1, results.Count, "Only the entity with TestAspectA should be yielded.");
        }

        [Test]
        public void QueryPair_YieldsOnlyEntitiesWithBothAspects()
        {
            NewWorld();
            var both = NewEntity("Both");
            both.Require<TestAspectA>();
            both.Require<TestAspectB>();

            var onlyA = NewEntity("OnlyA");
            onlyA.Require<TestAspectA>();

            var tuples = World.Query<TestAspectA, TestAspectB>().ToList();

            Assert.AreEqual(1, tuples.Count);
            Assert.AreSame(both, tuples[0].Entity);
            Assert.IsNotNull(tuples[0].First);
            Assert.IsNotNull(tuples[0].Second);
        }

        [Test]
        public void QueryTriple_YieldsOnlyEntitiesWithAllThreeAspects()
        {
            NewWorld();
            var all = NewEntity("AllThree");
            all.Require<TestAspectA>();
            all.Require<TestAspectB>();
            all.Require<TestAspectC>();

            var missingC = NewEntity("MissingC");
            missingC.Require<TestAspectA>();
            missingC.Require<TestAspectB>();

            var tuples = World.Query<TestAspectA, TestAspectB, TestAspectC>().ToList();

            Assert.AreEqual(1, tuples.Count);
            Assert.AreSame(all, tuples[0].Entity);
        }

        [Test]
        public void QueryEight_YieldsEntityCarryingAllEightAspects()
        {
            NewWorld();
            var entity = NewEntity("All8");
            entity.Require<A1>();
            entity.Require<A2>();
            entity.Require<A3>();
            entity.Require<A4>();
            entity.Require<A5>();
            entity.Require<A6>();
            entity.Require<A7>();
            entity.Require<A8>();

            var tuples = World.Query<A1, A2, A3, A4, A5, A6, A7, A8>().ToList();

            Assert.AreEqual(1, tuples.Count);
            Assert.AreSame(entity, tuples[0].Entity);
        }

        [Test]
        public void EntityDestruction_UnregistersFromWorld()
        {
            NewWorld();
            var entity = NewEntity();
            entity.Require<TestAspectA>();
            Assert.IsTrue(World.Query<TestAspectA>().Any(), "precondition: entity is indexed");

            var entityGo = entity.gameObject;
            DestroyWithLifecycle(entityGo);
            _spawned.Remove(entityGo);

            Assert.IsFalse(World.Query<TestAspectA>().Any());
        }

        [Test]
        public void Query_WithNoWorld_ReturnsEmpty()
        {
            Assert.IsNull(World.Instance, "precondition: no world exists");

            var results = World.Query<TestAspectA>().ToList();

            Assert.IsEmpty(results);
        }

        [Test]
        public void WorldAwakeAfterEntities_StillIndexesExistingAspects()
        {
            // Entity exists first, World spawned later — exercises the Awake safety-net scan.
            var entity = NewEntity();
            entity.Require<TestAspectA>();

            NewWorld();

            CollectionAssert.Contains(World.Query<TestAspectA>().ToList(), entity.Require<TestAspectA>());
        }

        [Test]
        public void World_IsItselfAnEntity_AcceptsAspects()
        {
            var world = NewWorld();

            var aspect = ((EntityContext)world).Require<TestAspectA>();

            Assert.IsNotNull(aspect);
            Assert.IsTrue(world.Has<TestAspectA>());
        }

        [Test]
        public void StaticRequire_ForwardsToInstance()
        {
            var world = NewWorld();

            var aspect = World.Require<TestAspectA>();

            Assert.AreSame(((EntityContext)world).Require<TestAspectA>(), aspect);
        }

        [Test]
        public void StaticRequire_WithNoWorld_Throws()
        {
            Assert.IsNull(World.Instance, "precondition: no world exists");

            Assert.Throws<System.InvalidOperationException>(() => World.Require<TestAspectA>());
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
        private class TestAspectC : IEntityAspect { }

        private class A1 : IEntityAspect { }
        private class A2 : IEntityAspect { }
        private class A3 : IEntityAspect { }
        private class A4 : IEntityAspect { }
        private class A5 : IEntityAspect { }
        private class A6 : IEntityAspect { }
        private class A7 : IEntityAspect { }
        private class A8 : IEntityAspect { }
    }
}
