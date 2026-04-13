using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class AspectInjectorTests
    {
        private GameObject _gameObject;
        private MonoEntity _context;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(AspectInjectorTests));
            _context = _gameObject.AddComponent<MonoEntity>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Inject_SinglePrivateReadonlyField_InjectsInstanceFromContext()
        {
            // Arrange
            var expected = _context.Require<TestAspectA>();
            var host = new SingleFieldHost();

            // Act
            AspectInjector.Inject(_context, host);

            // Assert — must be the exact instance owned by the context, not a fresh allocation.
            Assert.AreSame(expected, host.A);
        }

        [Test]
        public void Inject_MultipleFields_InjectsInstancesFromContext()
        {
            // Arrange
            var expectedA = _context.Require<TestAspectA>();
            var expectedB = _context.Require<TestAspectB>();
            var host = new MultipleFieldsHost();

            // Act
            AspectInjector.Inject(_context, host);

            // Assert
            Assert.AreSame(expectedA, host.A);
            Assert.AreSame(expectedB, host.B);
        }

        [Test]
        public void Inject_NonAspectField_LeavesUntouched()
        {
            // Arrange
            var expectedMarked = _context.Require<TestAspectA>();
            var host = new NonAspectFieldHost();

            // Act
            AspectInjector.Inject(_context, host);

            // Assert
            Assert.AreSame(expectedMarked, host.Marked);
            Assert.IsNull(host.Unmarked);
        }

        [Test]
        public void Inject_HostWithNoAspectFields_DoesNotThrow()
        {
            // Arrange
            var host = new EmptyHost();

            // Act & Assert
            Assert.DoesNotThrow(() => AspectInjector.Inject(_context, host));
        }

        [Test]
        public void Inject_InheritedAspectField_InjectsInstanceFromContext()
        {
            // Arrange
            var expected = _context.Require<TestAspectA>();
            var host = new DerivedHost();

            // Act
            AspectInjector.Inject(_context, host);

            // Assert — proves CollectAspectFields walks up the class hierarchy.
            Assert.AreSame(expected, host.Inherited);
        }

        [Test]
        public void Inject_DerivedHost_InjectsBothOwnAndInheritedFields()
        {
            // Arrange
            var expectedBase = _context.Require<TestAspectA>();
            var expectedOwn = _context.Require<TestAspectB>();
            var host = new DerivedHost();

            // Act
            AspectInjector.Inject(_context, host);

            // Assert
            Assert.AreSame(expectedBase, host.Inherited);
            Assert.AreSame(expectedOwn, host.Own);
        }

        [Test]
        public void Inject_IntoPureEntity_InjectsInstancesFromEntity()
        {
            // Proof that the reflection path now lives on IEntity, not MonoEntity:
            // the same injector must work against a pure POCO Entity with no
            // GameObject in sight. If this regresses, pure-core simulations lose
            // the ability to wire up aspect consumers.
            var entity = new Entity();
            var expected = entity.Require<TestAspectA>();
            var host = new SingleFieldHost();

            AspectInjector.Inject(entity, host);

            Assert.AreSame(expected, host.A);
        }

        [Test]
        public void Inject_ReadonlyField_AssignsThroughReflectionSet()
        {
            // Regression guard: AspectInjector must keep using FieldInfo.SetValue for the
            // write, because every [Aspect] field is declared `readonly` and Expression.Assign
            // on a Field node throws ArgumentException for initonly fields at Compile() time.
            // If this test starts failing with an ArgumentException from Expression.Lambda.Compile,
            // the injector has been "optimized" into a path that can't set readonly fields.
            var expected = _context.Require<TestAspectA>();
            var host = new SingleFieldHost();

            AspectInjector.Inject(_context, host);

            Assert.AreSame(expected, host.A);
        }

        [Test]
        public void Inject_SameAspectTypeOnTwoHosts_SharesInstanceViaContext()
        {
            // Arrange — pre-computing `expected` via the context proves that injection
            // routes through Context.Require rather than each host getting its own allocation.
            var expected = _context.Require<TestAspectA>();
            var host1 = new SingleFieldHost();
            var host2 = new SingleFieldHost();

            // Act
            AspectInjector.Inject(_context, host1);
            AspectInjector.Inject(_context, host2);

            // Assert
            Assert.AreSame(expected, host1.A);
            Assert.AreSame(expected, host2.A);
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }

        private class SingleFieldHost
        {
            [Aspect] private readonly TestAspectA _a = default!;
            public TestAspectA A => _a;
        }

        private class MultipleFieldsHost
        {
            [Aspect] private readonly TestAspectA _a = default!;
            [Aspect] private readonly TestAspectB _b = default!;
            public TestAspectA A => _a;
            public TestAspectB B => _b;
        }

        private class NonAspectFieldHost
        {
            [Aspect] private readonly TestAspectA _marked = default!;
            private readonly TestAspectB _unmarked = default!;
            public TestAspectA Marked => _marked;
            public TestAspectB Unmarked => _unmarked;
        }

        private class EmptyHost { }

        private class BaseHost
        {
            [Aspect] protected readonly TestAspectA _inherited = default!;
            public TestAspectA Inherited => _inherited;
        }

        private class DerivedHost : BaseHost
        {
            [Aspect] private readonly TestAspectB _own = default!;
            public TestAspectB Own => _own;
        }
    }
}
