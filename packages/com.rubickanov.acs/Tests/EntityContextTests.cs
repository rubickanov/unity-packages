using System.Reflection;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class EntityContextTests
    {
        private GameObject _gameObject;
        private EntityContext _context;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(EntityContextTests));
            _context = _gameObject.AddComponent<EntityContext>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
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
                Object.DestroyImmediate(worldGo);
                typeof(SingletonEntityContext<World>)
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                    .SetValue(null, null);
            }
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
