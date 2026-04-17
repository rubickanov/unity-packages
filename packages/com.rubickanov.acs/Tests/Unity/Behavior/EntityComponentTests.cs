using System;
using System.Reflection;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class EntityComponentTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
                UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Context_ParentHasNoMonoEntity_ThrowsInvalidOperationException()
        {
            _gameObject = new GameObject("OrphanComponent");
            var component = _gameObject.AddComponent<TestEntityComponent>();

            var ex = Assert.Throws<InvalidOperationException>(() => component.GetContext());

            StringAssert.Contains(nameof(TestEntityComponent), ex.Message);
            StringAssert.Contains("OrphanComponent", ex.Message);
            StringAssert.Contains("MonoEntity", ex.Message);
        }

        [Test]
        public void Context_ParentHasMonoEntity_ReturnsParentEntity()
        {
            _gameObject = new GameObject("ParentEntity");
            var parentEntity = _gameObject.AddComponent<MonoEntity>();

            var child = new GameObject("Child");
            child.transform.SetParent(_gameObject.transform);
            var component = child.AddComponent<TestEntityComponent>();

            Assert.AreSame(parentEntity, component.GetContext());
        }

        [Test]
        public void Context_ResolvedOnce_CachesResult()
        {
            _gameObject = new GameObject("ParentEntity");
            var parentEntity = _gameObject.AddComponent<MonoEntity>();

            var child = new GameObject("Child");
            child.transform.SetParent(_gameObject.transform);
            var component = child.AddComponent<TestEntityComponent>();

            var first = component.GetContext();
            var second = component.GetContext();

            Assert.AreSame(parentEntity, first);
            Assert.AreSame(first, second);
        }

        [Test]
        public void OnEnable_AfterOnDisable_ReSubscribesSubjects()
        {
            // Regression for the DisposableBag-is-a-latched-struct bug: the bag's internal
            // disposed flag stays set after Dispose, so any AddTo against the same bag would
            // immediately dispose the newly-added subscription. Without the `_disposables =
            // default` reset in OnEnable, the second Subscribe sinks into the void and no
            // handler fires. This test exercises the disable/re-enable cycle explicitly —
            // e.g. a pooled or SetActive-toggled component path.
            _gameObject = new GameObject("ParentEntity");
            _gameObject.AddComponent<MonoEntity>();

            var child = new GameObject("Child");
            child.transform.SetParent(_gameObject.transform);
            var component = child.AddComponent<ReSubscribingComponent>();

            InvokeLifecycle(component, "OnEnable");
            component.Signal.OnNext(Unit.Default);
            Assume.That(component.FireCount, Is.EqualTo(1),
                "Precondition: first OnEnable must wire a live subscription.");

            InvokeLifecycle(component, "OnDisable");
            InvokeLifecycle(component, "OnEnable");
            component.Signal.OnNext(Unit.Default);

            Assert.AreEqual(2, component.FireCount,
                "After disable/re-enable the subscription must be live again — a stale " +
                "DisposableBag would swallow the second Subscribe silently.");
        }

        [Test]
        public void Awake_RunsOnAwakeAfterInjection()
        {
            // OnAwake is the replacement extension point for subclasses that previously
            // overrode Awake. It must run AFTER [Aspect] injection so the subclass sees
            // its aspect fields already populated (injection order is covered by
            // AspectInjector tests; this test only pins the "OnAwake runs and runs after
            // Awake does its work" contract).
            _gameObject = new GameObject("ParentEntity");
            _gameObject.AddComponent<MonoEntity>();

            var child = new GameObject("Child");
            child.transform.SetParent(_gameObject.transform);
            var component = child.AddComponent<OnAwakeRecordingComponent>();

            InvokeLifecycle(component, "Awake");

            Assert.AreEqual(1, component.OnAwakeCalls,
                "OnAwake must be invoked from the base Awake exactly once.");
        }

        private static void InvokeLifecycle(MonoBehaviour target, string methodName)
        {
            typeof(EntityComponent)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(target, null);
        }

        // Test-only subclass: exposes the protected Context getter so we can assert
        // its null-handling directly. Unity does not fire Awake on AddComponent in EditMode,
        // so base.Awake (AspectInjector path) never runs here — no override-suppression is
        // needed, which matches the no-override-Awake contract EntityComponent now enforces.
        private sealed class TestEntityComponent : EntityComponent
        {
            public MonoEntity GetContext() => Context;
        }

        private sealed class OnAwakeRecordingComponent : EntityComponent
        {
            public int OnAwakeCalls;
            protected override void OnAwake() => OnAwakeCalls++;
        }

        private sealed class ReSubscribingComponent : EntityComponent
        {
            public readonly Subject<Unit> Signal = new();
            public int FireCount;

            protected override void OnSubscribe(ref DisposableBag disposables)
            {
                Signal.Subscribe(_ => FireCount++).AddTo(ref disposables);
            }
        }
    }
}
