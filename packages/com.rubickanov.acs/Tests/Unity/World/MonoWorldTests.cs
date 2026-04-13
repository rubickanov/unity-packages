using System;
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
    public class MonoWorldTests
    {
        // Unity does not fire Awake/OnDestroy on components in EditMode tests, so we invoke them
        // via reflection. SendMessage would also work but trips Unity's ShouldRunBehaviour gate
        // and produces log-assertion noise the test framework then reports as a failure.
        private static readonly MethodInfo MonoWorldAwakeMethod = typeof(MonoWorld).GetMethod(
            "Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!;
        private static readonly MethodInfo EntityOnDestroyMethod = typeof(MonoEntity).GetMethod(
            "OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!;
        private static readonly PropertyInfo MonoWorldInstanceProp = typeof(SingletonMonoEntity<MonoWorld>)
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo ForceResetCurrentMethod = typeof(World).GetMethod(
            "ForceResetCurrent", BindingFlags.NonPublic | BindingFlags.Static)!;

        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            // Clear both the Mono singleton slot AND the pure World.Current slot, in case a
            // previous test failed mid-lifecycle and left them populated with a Unity-destroyed
            // reference that still passes `!= null` checks but isn't a true null.
            ResetMonoWorldInstance();
            ForceResetCurrent();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                DestroyWithLifecycle(go);
            _spawned.Clear();
            ResetMonoWorldInstance();
            ForceResetCurrent();
        }

        private MonoWorld NewMonoWorld(string name = "MonoWorld")
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            var world = go.AddComponent<MonoWorld>();
            MonoWorldAwakeMethod.Invoke(world, null);
            return world;
        }

        private MonoEntity NewEntity(string name = "Entity")
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<MonoEntity>();
        }

        // Destroys the GameObject the way Unity would at runtime: invoke OnDestroy on every
        // MonoEntity on it (so World.Unregister / ClearCurrent / Dispose fire) before the actual
        // DestroyImmediate call. Safe to pass already-destroyed or null references.
        private static void DestroyWithLifecycle(GameObject go)
        {
            if (go == null)
                return;
            foreach (var ec in go.GetComponents<MonoEntity>())
                EntityOnDestroyMethod.Invoke(ec, null);
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static void ResetMonoWorldInstance() => MonoWorldInstanceProp.SetValue(null, null);

        private static void ForceResetCurrent() => ForceResetCurrentMethod.Invoke(null, null);

        [Test]
        public void Awake_FirstInstance_SetsStaticReferences()
        {
            var world = NewMonoWorld();

            Assert.AreSame(world, MonoWorld.Instance);
            Assert.AreSame(world.World, World.Current);
        }

        [Test]
        public void Awake_DuplicateInstance_LeavesOriginalAsInstance()
        {
            var first = NewMonoWorld("FirstWorld");

            // The duplicate's Awake calls Destroy(gameObject); in EditMode Unity downgrades
            // that to a warning. We don't care that the GO survives — we care that Instance
            // still points at the first world.
            LogAssert.ignoreFailingMessages = true;
            NewMonoWorld("SecondWorld");
            LogAssert.ignoreFailingMessages = false;

            Assert.AreSame(first, MonoWorld.Instance);
            Assert.AreSame(first.World, World.Current);
        }

        [Test]
        public void OnDestroy_ClearsStaticReferences()
        {
            var world = NewMonoWorld();
            var go = world.gameObject;

            DestroyWithLifecycle(go);
            _spawned.Remove(go);

            Assert.IsNull(MonoWorld.Instance);
            Assert.IsNull(World.Current);
        }

        [Test]
        public void Require_OnEntityWithWorldPresent_AutoRegistersWithIndex()
        {
            NewMonoWorld();
            var entity = NewEntity();

            entity.Require<TestAspectA>();

            CollectionAssert.Contains(MonoWorld.Instance!.World.Registry.GetAllWith(typeof(TestAspectA)), entity);
        }

        [Test]
        public void QuerySingle_YieldsAspectFromEveryEntity()
        {
            NewMonoWorld();
            var a = NewEntity("A").Require<TestAspectA>();
            var b = NewEntity("B").Require<TestAspectA>();

            var all = World.Query<TestAspectA>().ToList();

            CollectionAssert.AreEquivalent(new[] { a, b }, all);
        }

        [Test]
        public void QuerySingle_SkipsEntitiesWithoutAspect()
        {
            NewMonoWorld();
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
            NewMonoWorld();
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
            NewMonoWorld();
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
            NewMonoWorld();
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
            NewMonoWorld();
            var entity = NewEntity();
            entity.Require<TestAspectA>();
            Assert.IsTrue(World.Query<TestAspectA>().Any(), "precondition: entity is indexed");

            var entityGo = entity.gameObject;
            DestroyWithLifecycle(entityGo);
            _spawned.Remove(entityGo);

            Assert.IsFalse(World.Query<TestAspectA>().Any());
        }

        [Test]
        public void Query_WithNoWorld_Throws()
        {
            Assert.IsNull(World.Current, "precondition: no world exists");

            Assert.Throws<InvalidOperationException>(() => World.Query<TestAspectA>());
        }

        [Test]
        public void MonoWorld_IsItselfAnEntity_AcceptsAspects()
        {
            var world = NewMonoWorld();

            var aspect = ((MonoEntity)world).Require<TestAspectA>();

            Assert.IsNotNull(aspect);
            Assert.IsTrue(world.Has<TestAspectA>());
        }

        [Test]
        public void StaticRequire_ForwardsToCurrent()
        {
            var world = NewMonoWorld();

            var aspect = World.Require<TestAspectA>();

            // MonoWorld delegates MonoEntity.Require into _world, and World.Require<T>() also
            // lands on the same _world via Current — both paths must produce the same instance.
            Assert.AreSame(((MonoEntity)world).Require<TestAspectA>(), aspect);
            Assert.AreSame(((IEntity)world.World).Require<TestAspectA>(), aspect);
        }

        [Test]
        public void StaticRequire_WithNoWorld_Throws()
        {
            Assert.IsNull(World.Current, "precondition: no world exists");

            Assert.Throws<InvalidOperationException>(() => World.Require<TestAspectA>());
        }

        [Test]
        public void OnAspectCreated_FiresForWorldScopedAspect_ViaStaticRequire()
        {
            // Public contract: MonoEntity.OnAspectCreated must fire for any newly-created
            // aspect, including ones that live on the world itself — MonoWorld subscribes
            // to pure World's AspectCreated and forwards into the static MonoEntity event.
            NewMonoWorld();
            IEntity observedEntity = null;
            Type observedType = null;
            Action<IEntity, Type> handler = (e, t) => { observedEntity = e; observedType = t; };
            MonoEntity.OnAspectCreated += handler;
            try
            {
                World.Require<TestAspectA>();
            }
            finally
            {
                MonoEntity.OnAspectCreated -= handler;
            }

            Assert.IsNotNull(observedEntity, "OnAspectCreated must fire for world-scoped aspects.");
            Assert.AreEqual(typeof(TestAspectA), observedType);
        }

        [Test]
        public void OnAspectCreated_FiresForWorldScopedAspect_ViaInstanceRequire()
        {
            var world = NewMonoWorld();
            IEntity observedEntity = null;
            Action<IEntity, Type> handler = (e, _) => observedEntity = e;
            MonoEntity.OnAspectCreated += handler;
            try
            {
                world.Require<TestAspectA>();
            }
            finally
            {
                MonoEntity.OnAspectCreated -= handler;
            }

            Assert.IsNotNull(observedEntity,
                "MonoWorld.Require delegates to the pure World — the forward must still fire the MonoEntity.OnAspectCreated event.");
        }

        [Test]
        public void DuplicateMonoWorld_DisposesItsEmbeddedWorld()
        {
            // A duplicate MonoWorld self-destroys in Awake before it can become Instance,
            // but its field-initializer `_world = new()` already ran. OnDestroy must Dispose
            // that orphan so it doesn't leak — we observe Dispose via the Destroyed event.
            NewMonoWorld("First");

            var dupGo = new GameObject("Dup");
            _spawned.Add(dupGo);
            LogAssert.ignoreFailingMessages = true;
            var dup = dupGo.AddComponent<MonoWorld>();
            MonoWorldAwakeMethod.Invoke(dup, null);
            LogAssert.ignoreFailingMessages = false;

            var duplicateWorld = dup.World;
            var destroyed = 0;
            duplicateWorld.Destroyed += _ => destroyed++;

            // Simulate Unity's delayed OnDestroy for the self-destroyed duplicate.
            EntityOnDestroyMethod.Invoke(dup, null);

            Assert.AreEqual(1, destroyed,
                "Duplicate MonoWorld.OnDestroy must Dispose the embedded _world even though Instance != this.");
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
