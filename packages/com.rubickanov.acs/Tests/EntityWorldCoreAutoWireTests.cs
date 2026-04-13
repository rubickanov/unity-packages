using System.Linq;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Edit-mode tests that pin the auto-wire contract between pure
    /// <see cref="Entity"/> and <see cref="WorldCore"/> when the entity is
    /// constructed via the <see cref="Entity(WorldCore)"/> overload. The
    /// ergonomic path is a mirror of how <see cref="MonoEntity"/> auto-integrates
    /// with <c>World.Instance</c> — this fixture guards it from regressing back
    /// to the manual <c>core.Register</c> / <c>core.Unregister</c> dance.
    /// </summary>
    [TestFixture]
    public class EntityWorldCoreAutoWireTests
    {
        [Test]
        public void Ctor_WithCore_Require_AutoRegisters()
        {
            var core = new WorldCore();
            var entity = new Entity(core);

            var aspect = entity.Require<TestAspectA>();

            CollectionAssert.AreEqual(new[] { aspect }, core.Query<TestAspectA>().ToList());
        }

        [Test]
        public void Ctor_WithCore_RequireTwice_RegistersOnce()
        {
            var core = new WorldCore();
            var entity = new Entity(core);

            entity.Require<TestAspectA>();
            entity.Require<TestAspectA>();

            Assert.AreEqual(1, core.Registry.GetAllWith(typeof(TestAspectA)).Count);
        }

        [Test]
        public void Ctor_WithCore_Dispose_AutoUnregisters()
        {
            var core = new WorldCore();
            var entity = new Entity(core);
            entity.Require<TestAspectA>();
            entity.Require<TestAspectB>();

            entity.Dispose();

            Assert.IsEmpty(core.Query<TestAspectA>().ToList());
            Assert.IsEmpty(core.Query<TestAspectB>().ToList());
        }

        [Test]
        public void Ctor_WithCore_Dispose_DestroyedSubscribersCanStillQueryRegistry()
        {
            // Ordering invariant: Destroyed must fire BEFORE core.Unregister so
            // subscribers can run one last query while unwinding. Same contract
            // as MonoEntity.OnDestroy.
            var core = new WorldCore();
            var entity = new Entity(core);
            entity.Require<TestAspectA>();
            var visibleInsideHandler = false;

            entity.Destroyed += _ => visibleInsideHandler = core.Query<TestAspectA>().Any();
            entity.Dispose();

            Assert.IsTrue(visibleInsideHandler,
                "Destroyed handler must see the entity still in the registry — core.Unregister should run after the event fires.");
        }

        [Test]
        public void Ctor_WithoutCore_Dispose_DoesNotTouchCore()
        {
            // Backward-compat: entities created via the parameterless ctor keep
            // the manual-registration contract. Dispose must not silently drop
            // the entity from a core it was registered with externally.
            var core = new WorldCore();
            var entity = new Entity();
            entity.Require<TestAspectA>();
            core.Register(entity, typeof(TestAspectA));

            entity.Dispose();

            Assert.AreEqual(1, core.Registry.GetAllWith(typeof(TestAspectA)).Count,
                "no-core Entity must not auto-unregister — callers are still responsible for Unregister.");
        }

        [Test]
        public void Ctor_WithCore_DisposeTwice_IsNoOp()
        {
            var core = new WorldCore();
            var entity = new Entity(core);
            entity.Require<TestAspectA>();
            var fireCount = 0;
            entity.Destroyed += _ => fireCount++;

            entity.Dispose();
            entity.Dispose();

            Assert.AreEqual(1, fireCount, "Destroyed must fire at most once.");
            Assert.IsEmpty(core.Query<TestAspectA>().ToList());
        }

        [Test]
        public void Ctor_WithCore_ManualRegister_NoDoubleBucket()
        {
            // Defensive: if a caller mixes manual Register with auto-register
            // the registry's HashSet-based dedup keeps bucket size at 1.
            var core = new WorldCore();
            var entity = new Entity(core);

            core.Register(entity, typeof(TestAspectA));
            entity.Require<TestAspectA>();

            Assert.AreEqual(1, core.Registry.GetAllWith(typeof(TestAspectA)).Count);
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
