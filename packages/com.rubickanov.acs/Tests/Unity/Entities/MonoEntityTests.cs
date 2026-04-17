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
            // Defensive reset: CurrentChanged can carry stale subscriptions from a MonoEntity
            // that Awoke without a world in this test. A subsequent test that spawns a MonoWorld
            // would otherwise fire the handler on a destroyed object and MissingReferenceException
            // its way through unrelated coverage.
            ResetMonoWorldStatics();
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
        public void DefaultExecutionOrder_IsLowerThanUserComponentsAndHigherThanMonoWorld()
        {
            // Unity doesn't guarantee Awake ordering between parent and child GameObjects;
            // without explicit execution order, a child EntityComponent (user code, default 0)
            // can Awake before this MonoEntity on the parent and call Require<T> on an entity
            // whose Id is still EntityId.None. Subscribers to AspectCreated keyed by Id then
            // get bogus keys. Pin the invariant in metadata so a refactor can't silently drop it.
            var monoEntityOrder = typeof(MonoEntity)
                .GetCustomAttribute<DefaultExecutionOrder>()!;
            var monoWorldOrder = typeof(MonoWorld)
                .GetCustomAttribute<DefaultExecutionOrder>()!;

            Assert.NotNull(monoEntityOrder, "MonoEntity must declare [DefaultExecutionOrder] — see file header for why.");
            Assert.Less(monoEntityOrder.order, 0,
                "MonoEntity execution order must be negative so it runs before any default-order user component.");
            Assert.Greater(monoEntityOrder.order, monoWorldOrder.order,
                "MonoEntity must run AFTER MonoWorld so World.Current is already set during MonoEntity.Awake's by-id registration.");
        }

        [Test]
        public void Awake_Always_AssignsNonNoneId()
        {
            InvokeAwake(_context);

            Assert.IsFalse(_context.Id.IsNone,
                "Awake must allocate an id regardless of whether World.Current is set.");
        }

        [Test]
        public void Awake_WhenCurrentWorldIsNull_AllocatesIdAndDefersRegistration()
        {
            InvokeAwake(_context);

            // With no world present, Awake still allocates the id (unconditional) but defers
            // the by-id registration to whenever a world becomes Current (see
            // Awake_WithoutWorld_ThenWorldBecomesCurrent_RegistersRetroactively). This test
            // pins the "Awake doesn't throw when World.Current is null" half of the contract.
            Assert.IsFalse(_context.Id.IsNone);
        }

        [Test]
        public void Awake_WithoutWorld_ThenWorldBecomesCurrent_RegistersRetroactively()
        {
            // Regression for the invariant leak: spawn a MonoEntity into a scene with no
            // MonoWorld, then introduce a MonoWorld later. Before Batch 6 the entity was
            // silently unreachable via Query / TryFindById — "If no world is set at Awake
            // time, the entity is never retroactively registered" was documented but easy to
            // shoot yourself with. The CurrentChanged event closes this gap.
            InvokeAwake(_context);
            Assume.That(World.Current, Is.Null, "Precondition: no world active before the MonoWorld is introduced.");

            var worldGo = new GameObject(nameof(MonoWorld));
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                InvokeAwake(world);

                Assert.IsTrue(MonoWorld.Instance!.World.TryFindById(_context.Id, out var resolved),
                    "MonoEntity that Awoke before World.Current was assigned must be retroactively registered when a world becomes current.");
                Assert.AreSame(_context, resolved);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldGo);
                ResetMonoWorldStatics();
            }
        }

        [Test]
        public void Awake_WithoutWorld_RequiredAspects_FlowIntoWorldOnCurrentChanged()
        {
            // The aspects created during the no-world window were previously invisible to the
            // world's per-aspect index forever. Batch 6 makes them flow into the registry the
            // moment a world appears, so subsequent Query<T> calls find the entity.
            InvokeAwake(_context);
            _context.Require<TestAspectA>();
            _context.Require<TestAspectB>();

            var worldGo = new GameObject(nameof(MonoWorld));
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                InvokeAwake(world);

                var registry = MonoWorld.Instance!.World.Registry;
                CollectionAssert.Contains(registry.GetAllWith(typeof(TestAspectA)), _context,
                    "TestAspectA created before the world existed must be registered in the per-aspect index when the world becomes current.");
                CollectionAssert.Contains(registry.GetAllWith(typeof(TestAspectB)), _context,
                    "TestAspectB likewise — retroactive registration must cover every aspect already in the store.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldGo);
                ResetMonoWorldStatics();
            }
        }

        [Test]
        public void OnDestroy_BeforeWorldBecomesCurrent_UnsubscribesPendingHandler()
        {
            // An entity that Awakens without a world subscribes to CurrentChanged and then
            // gets destroyed before any world appears. The handler must be dropped so a later
            // SetCurrent cannot invoke a method on a destroyed MonoBehaviour — which would
            // throw MissingReferenceException and surface as a test failure here.
            InvokeAwake(_context);
            InvokeOnDestroy(_context);

            var worldGo = new GameObject(nameof(MonoWorld));
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                Assert.DoesNotThrow(() => InvokeAwake(world),
                    "MonoWorld.Awake must not invoke a handler on the already-destroyed MonoEntity — OnDestroy must have unsubscribed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldGo);
                ResetMonoWorldStatics();
            }
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
            // Batch 6: CurrentChanged subscribers must not leak across tests — see TearDown
            // for the failure mode this prevents.
            typeof(World)
                .GetMethod("ResetStaticEvents", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
