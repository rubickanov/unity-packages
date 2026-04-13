using System;
using NUnit.Framework;
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

        // Test-only subclass: exposes the protected Context getter so we can assert
        // its null-handling directly without relying on Unity firing Awake in EditMode
        // (which MonoEntityTests notes it does not do for AddComponent).
        private sealed class TestEntityComponent : EntityComponent
        {
            public MonoEntity GetContext() => Context;

            protected override void Awake()
            {
                // Suppress base Awake to avoid AspectInjector.Inject on a potentially
                // null Context during AddComponent — these tests drive Context explicitly.
            }
        }
    }
}
