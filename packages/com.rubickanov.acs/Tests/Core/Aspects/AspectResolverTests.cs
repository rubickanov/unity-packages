using System;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class AspectResolverTests
    {
        private Entity _entity;

        [SetUp]
        public void SetUp()
        {
            _entity = new Entity();
        }

        [TearDown]
        public void TearDown()
        {
            _entity.Dispose();
        }

        [Test]
        public void Require_AspectType_ReturnsSameInstanceAsGenericRequire()
        {
            var expected = _entity.Require<TestAspectA>();

            var actual = AspectResolver.Require(_entity, typeof(TestAspectA));

            Assert.AreSame(expected, actual);
        }

        [Test]
        public void Require_AspectNotYetPresent_CreatesIt()
        {
            var resolved = AspectResolver.Require(_entity, typeof(TestAspectA));

            Assert.IsInstanceOf<TestAspectA>(resolved);
            Assert.IsTrue(_entity.Has<TestAspectA>());
        }

        [Test]
        public void Require_SameTypeTwice_ReturnsSameInstance()
        {
            // Second call goes through the cached dispatcher rather than building a new one;
            // routing through IEntity.Require keeps it idempotent either way.
            var first = AspectResolver.Require(_entity, typeof(TestAspectA));
            var second = AspectResolver.Require(_entity, typeof(TestAspectA));

            Assert.AreSame(first, second);
        }

        [Test]
        public void Require_SameTypeOnTwoEntities_ReturnsPerEntityInstances()
        {
            // Guards against the dispatcher accidentally caching the aspect instead of the
            // call — the cache is keyed by Type and must stay entity-agnostic.
            var other = new Entity();
            try
            {
                var mine = AspectResolver.Require(_entity, typeof(TestAspectA));
                var theirs = AspectResolver.Require(other, typeof(TestAspectA));

                Assert.AreNotSame(mine, theirs);
            }
            finally
            {
                other.Dispose();
            }
        }

        [Test]
        public void Require_MonoEntityContext_ResolvesThroughSameCache()
        {
            var go = new GameObject(nameof(AspectResolverTests));
            try
            {
                var context = go.AddComponent<MonoEntity>();
                var expected = context.Require<TestAspectA>();

                var actual = AspectResolver.Require(context, typeof(TestAspectA));

                Assert.AreSame(expected, actual);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Require_NullContext_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AspectResolver.Require(null, typeof(TestAspectA)));
        }

        [Test]
        public void Require_NullAspectType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AspectResolver.Require(_entity, null));
        }

        [Test]
        public void Require_TypeNotImplementingIEntityAspect_ThrowsNamingTheType()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => AspectResolver.Require(_entity, typeof(NotAnAspect)));

            Assert.That(ex.Message, Does.Contain(nameof(NotAnAspect)));
            Assert.That(ex.Message, Does.Contain(nameof(IEntityAspect)));
        }

        [Test]
        public void Require_AspectWithoutParameterlessCtor_ThrowsNamingTheType()
        {
            // The `new()` half of the Require<T> constraint. Without the explicit guard this
            // surfaces as a bare ArgumentException from MakeGenericType naming nothing useful.
            var ex = Assert.Throws<InvalidOperationException>(
                () => AspectResolver.Require(_entity, typeof(NoParameterlessCtorAspect)));

            Assert.That(ex.Message, Does.Contain(nameof(NoParameterlessCtorAspect)));
        }

        [Test]
        public void Require_AbstractAspectType_ThrowsNamingTheType()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => AspectResolver.Require(_entity, typeof(AbstractAspect)));

            Assert.That(ex.Message, Does.Contain(nameof(AbstractAspect)));
        }

        [Test]
        public void Require_StructType_ThrowsNamingTheType()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => AspectResolver.Require(_entity, typeof(StructAspect)));

            Assert.That(ex.Message, Does.Contain(nameof(StructAspect)));
        }

        [Test]
        public void Require_OpenGenericAspectType_ThrowsNamingTheType()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => AspectResolver.Require(_entity, typeof(GenericAspect<>)));

            Assert.That(ex.Message, Does.Contain("GenericAspect"));
        }

        [Test]
        public void Require_ClosedGenericAspectType_Resolves()
        {
            var resolved = AspectResolver.Require(_entity, typeof(GenericAspect<int>));

            Assert.IsInstanceOf<GenericAspect<int>>(resolved);
        }

        private class TestAspectA : IEntityAspect { }

        private class NotAnAspect { }

        private class NoParameterlessCtorAspect : IEntityAspect
        {
            public NoParameterlessCtorAspect(int _) { }
        }

        private abstract class AbstractAspect : IEntityAspect { }

        private struct StructAspect : IEntityAspect { }

        private class GenericAspect<T> : IEntityAspect { }
    }
}
