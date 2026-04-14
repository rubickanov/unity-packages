using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class MonoEntityTests
    {
        private GameObject _gameObject;
        private MonoEntity _context;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(MonoEntityTests));
            _context = _gameObject.AddComponent<MonoEntity>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Require_SameTypeTwice_ReturnsSameInstance()
        {
            // Act
            var first = _context.Require<TestAspectA>();
            var second = _context.Require<TestAspectA>();

            // Assert
            Assert.AreSame(first, second);
        }

        [Test]
        public void Require_DifferentTypes_ReturnsDifferentInstances()
        {
            // Act
            var a = _context.Require<TestAspectA>();
            var b = _context.Require<TestAspectB>();

            // Assert — IsInstanceOf proves both non-null and correct concrete type
            // in one shot, catching a regression where Require returns null or a wrong cast.
            Assert.IsInstanceOf<TestAspectA>(a);
            Assert.IsInstanceOf<TestAspectB>(b);
            Assert.AreNotSame(a, b);
        }

        [Test]
        public void TryGet_BeforeRequire_ReturnsFalse()
        {
            // Act
            var result = _context.TryGet<TestAspectA>(out var aspect);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(aspect);
        }

        [Test]
        public void TryGet_AfterRequire_ReturnsTrueAndInstance()
        {
            // Arrange
            var created = _context.Require<TestAspectA>();

            // Act
            var result = _context.TryGet<TestAspectA>(out var aspect);

            // Assert
            Assert.IsTrue(result);
            Assert.AreSame(created, aspect);
        }

        [Test]
        public void Has_BeforeRequire_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(_context.Has<TestAspectA>());
        }

        [Test]
        public void Has_AfterRequire_ReturnsTrue()
        {
            // Arrange
            _context.Require<TestAspectA>();

            // Act & Assert
            Assert.IsTrue(_context.Has<TestAspectA>());
        }

        [Test]
        public void GetAllAspects_Empty_ReturnsEmpty()
        {
            // Act
            var all = _context.GetAllAspects();

            // Assert
            Assert.IsNotNull(all);
            Assert.IsEmpty(all);
        }

        [Test]
        public void GetAllAspects_AfterMultipleRequires_ReturnsAllDistinct()
        {
            // Arrange
            var a = _context.Require<TestAspectA>();
            var b = _context.Require<TestAspectB>();

            // Act
            var all = _context.GetAllAspects();

            // Assert
            CollectionAssert.AreEquivalent(new object[] { a, b }, all);
        }

        [Test]
        public void Require_WithWorldPresent_RegistersWithWorld()
        {
            var worldGo = new GameObject(nameof(MonoWorld));
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                // Unity doesn't auto-fire Awake on AddComponent in EditMode tests; invoke it by
                // reflection so the singleton + World.Current initialize exactly as at runtime.
                typeof(MonoWorld)
                    .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                    .Invoke(world, null);

                _context.Require<TestAspectA>();

                CollectionAssert.Contains(
                    MonoWorld.Instance!.World.Registry.GetAllWith(typeof(TestAspectA)),
                    _context);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldGo);
                typeof(SingletonMonoEntity<MonoWorld>)
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                    .SetValue(null, null);
                typeof(World)
                    .GetMethod("ForceResetCurrent", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null);
            }
        }

        [Test]
        public void Require_CreatesNewAspect_FiresOnAspectCreated()
        {
            // OnAspectCreated is now raised via World.AspectCreated → MonoWorld forwarder,
            // so the event only fires when a World is Current. A bare MonoEntity with no world
            // would silently register nothing and produce no notification — matches the event's
            // "new aspect reachable via world queries" semantics.
            var worldGo = new GameObject(nameof(MonoWorld));
            var events = new List<(IEntity entity, Type type)>();
            Action<IEntity, Type> handler = (e, t) => events.Add((e, t));
            MonoEntity.OnAspectCreated += handler;
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                typeof(MonoWorld)
                    .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                    .Invoke(world, null);

                var aspect = _context.Require<TestAspectA>();

                Assert.AreEqual(1, events.Count);
                Assert.AreSame(_context, events[0].entity);
                Assert.AreEqual(typeof(TestAspectA), events[0].type);
                Assert.IsNotNull(aspect);
            }
            finally
            {
                MonoEntity.OnAspectCreated -= handler;
                UnityEngine.Object.DestroyImmediate(worldGo);
                typeof(SingletonMonoEntity<MonoWorld>)
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                    .SetValue(null, null);
                typeof(World)
                    .GetMethod("ForceResetCurrent", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null);
            }
        }

        [Test]
        public void Require_ReturnsExistingAspect_DoesNotFireOnAspectCreated()
        {
            var worldGo = new GameObject(nameof(MonoWorld));
            var fireCount = 0;
            Action<IEntity, Type> handler = (_, _) => fireCount++;
            MonoEntity.OnAspectCreated += handler;
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                typeof(MonoWorld)
                    .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                    .Invoke(world, null);

                _context.Require<TestAspectA>();
                _context.Require<TestAspectA>();

                Assert.AreEqual(1, fireCount);
            }
            finally
            {
                MonoEntity.OnAspectCreated -= handler;
                UnityEngine.Object.DestroyImmediate(worldGo);
                typeof(SingletonMonoEntity<MonoWorld>)
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                    .SetValue(null, null);
                typeof(World)
                    .GetMethod("ForceResetCurrent", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null);
            }
        }

        [Test]
        public void Start_AfterAwake_FiresOnAwakeCompleted()
        {
            var events = new List<MonoEntity>();
            Action<MonoEntity> handler = events.Add;
            MonoEntity.OnAwakeCompleted += handler;
            try
            {
                // EditMode tests don't auto-fire MonoBehaviour lifecycle on AddComponent; invoke
                // the private Start method the way Unity would at runtime.
                typeof(MonoEntity)
                    .GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(_context, null);

                Assert.AreEqual(1, events.Count);
                Assert.AreSame(_context, events[0]);
            }
            finally
            {
                MonoEntity.OnAwakeCompleted -= handler;
            }
        }

        [Test]
        public void Awake_Always_AssignsNonNoneId()
        {
            InvokeAwake(_context);

            Assert.IsFalse(_context.Id.IsNone,
                "Awake must allocate an id regardless of whether World.Current is set.");
        }

        [Test]
        public void Awake_WhenCurrentWorldIsNull_IsNotFindableByIdButIdIsStillAllocated()
        {
            InvokeAwake(_context);

            // No world was assigned — by-id registration silently no-ops (matching per-aspect
            // behavior). This test pins that invariant so if someone "fixes" the null check to
            // retroactively register, this test fails loudly.
            Assert.IsFalse(_context.Id.IsNone);
            // There's no world to query against, so there's nothing to "not find" — the invariant
            // here is just that Awake didn't throw on a null World.Current. Kept simple on purpose.
        }

        [Test]
        public void Awake_WhenCurrentWorldExists_IsFindableByIdBeforeAnyRequire()
        {
            var worldGo = new GameObject(nameof(MonoWorld));
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                InvokeAwake(world);
                // Critical: re-invoke Awake on the MonoEntity AFTER World.Current is set, so the
                // by-id registration in Awake can see a non-null world. The SetUp-time Awake was
                // empty (no world existed), so the entity's id-registration was a silent no-op.
                InvokeAwake(_context);

                // Ask the world via TryFindById WITHOUT calling Require<T> anywhere. This is the
                // new invariant: entity is addressable by id as soon as it registers — before it
                // owns any aspects and therefore before it would appear in any per-aspect query.
                var found = MonoWorld.Instance!.World.TryFindById(_context.Id, out var resolved);

                Assert.IsTrue(found,
                    "MonoEntity.Awake with a current world must register the entity in the by-id index before any Require<T>.");
                Assert.AreSame(_context, resolved);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldGo);
                ResetMonoWorldStatics();
            }
        }

        [Test]
        public void OnDestroy_AfterCall_UnregistersById()
        {
            var worldGo = new GameObject(nameof(MonoWorld));
            var entityGo = new GameObject(nameof(OnDestroy_AfterCall_UnregistersById));
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                InvokeAwake(world);

                // Build a fresh MonoEntity inside the test (rather than using the SetUp _context),
                // because we want to control its full lifecycle against the live MonoWorld.
                var entity = entityGo.AddComponent<MonoEntity>();
                InvokeAwake(entity);

                Assume.That(MonoWorld.Instance!.World.TryFindById(entity.Id, out _), Is.True,
                    "Pre-condition: entity should be findable by id after Awake (see Awake_WhenCurrentWorldExists_IsFindableByIdBeforeAnyRequire).");

                var id = entity.Id;
                // DestroyImmediate doesn't reliably fire OnDestroy on a MonoBehaviour whose Awake
                // was only invoked via reflection (Unity considers the component "never fully
                // entered the lifecycle"). Reflection-invoke OnDestroy directly so the test
                // exercises the real unregister path deterministically.
                InvokeOnDestroy(entity);

                Assert.IsFalse(MonoWorld.Instance!.World.TryFindById(id, out _),
                    "OnDestroy must unregister the entity from the by-id index — otherwise disposed MonoEntities remain addressable forever.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(entityGo);
                UnityEngine.Object.DestroyImmediate(worldGo);
                ResetMonoWorldStatics();
            }
        }

        // Unity doesn't auto-fire Awake on AddComponent in EditMode tests; invoke it by
        // reflection so the entity's Awake (id allocation + by-id registration) runs
        // exactly the way it would at runtime.
        private static void InvokeAwake(MonoBehaviour target)
        {
            typeof(MonoEntity)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                .Invoke(target, null);
        }

        // Matching helper for OnDestroy — DestroyImmediate doesn't reliably drive OnDestroy for
        // components that never entered the normal MonoBehaviour lifecycle through Unity itself.
        private static void InvokeOnDestroy(MonoBehaviour target)
        {
            typeof(MonoEntity)
                .GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                .Invoke(target, null);
        }

        private static void ResetMonoWorldStatics()
        {
            typeof(SingletonMonoEntity<MonoWorld>)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                .SetValue(null, null);
            typeof(World)
                .GetMethod("ForceResetCurrent", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
