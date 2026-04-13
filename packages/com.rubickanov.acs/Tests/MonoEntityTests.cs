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
            var worldGo = new GameObject(nameof(World));
            try
            {
                var world = worldGo.AddComponent<World>();
                // Unity doesn't auto-fire Awake on AddComponent in EditMode tests; invoke it by
                // reflection so the World singleton initializes exactly as it would at runtime.
                typeof(World)
                    .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                    .Invoke(world, null);

                _context.Require<TestAspectA>();

                CollectionAssert.Contains(
                    World.Instance!.Registry.GetAllWith(typeof(TestAspectA)),
                    _context);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldGo);
                typeof(SingletonMonoEntity<World>)
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                    .SetValue(null, null);
            }
        }

        [Test]
        public void Require_CreatesNewAspect_FiresOnAspectCreated()
        {
            var events = new List<(IEntity entity, Type type)>();
            Action<IEntity, Type> handler = (e, t) => events.Add((e, t));
            MonoEntity.OnAspectCreated += handler;
            try
            {
                var aspect = _context.Require<TestAspectA>();

                Assert.AreEqual(1, events.Count);
                Assert.AreSame(_context, events[0].entity);
                Assert.AreEqual(typeof(TestAspectA), events[0].type);
                Assert.IsNotNull(aspect);
            }
            finally
            {
                MonoEntity.OnAspectCreated -= handler;
            }
        }

        [Test]
        public void Require_ReturnsExistingAspect_DoesNotFireOnAspectCreated()
        {
            var fireCount = 0;
            Action<IEntity, Type> handler = (_, _) => fireCount++;
            MonoEntity.OnAspectCreated += handler;
            try
            {
                _context.Require<TestAspectA>();
                _context.Require<TestAspectA>();

                Assert.AreEqual(1, fireCount);
            }
            finally
            {
                MonoEntity.OnAspectCreated -= handler;
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

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
