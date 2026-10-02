using System.Linq;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Edit-mode tests that pin the auto-wire contract between pure
    /// <see cref="Entity"/> and <see cref="World"/> when the entity is
    /// constructed via the <see cref="Entity(World)"/> overload. The
    /// ergonomic path is a mirror of how <see cref="MonoEntity"/> auto-integrates
    /// with <see cref="World.Current"/> — this fixture guards it from regressing
    /// back to the manual <c>world.Register</c> / <c>world.Unregister</c> dance.
    /// </summary>
    [TestFixture]
    public class EntityWorldAutoWireTests
    {
        [Test]
        public void Ctor_WithWorld_Require_AutoRegisters()
        {
            var world = new World();
            var entity = new Entity(world);

            var aspect = entity.Require<TestAspectA>();

            CollectionAssert.AreEqual(new[] { aspect }, world.QueryLocal<TestAspectA>().ToList());
        }

        [Test]
        public void Ctor_WithWorld_RequireTwice_RegistersOnce()
        {
            var world = new World();
            var entity = new Entity(world);

            entity.Require<TestAspectA>();
            entity.Require<TestAspectA>();

            Assert.AreEqual(1, world.Registry.GetAllWith(typeof(TestAspectA)).Count);
        }

        [Test]
        public void Ctor_WithWorld_Dispose_AutoUnregisters()
        {
            var world = new World();
            var entity = new Entity(world);
            entity.Require<TestAspectA>();
            entity.Require<TestAspectB>();

            entity.Dispose();

            Assert.IsEmpty(world.QueryLocal<TestAspectA>().ToList());
            Assert.IsEmpty(world.QueryLocal<TestAspectB>().ToList());
        }

        [Test]
        public void Ctor_WithWorld_Dispose_DestroyedSubscribersCanStillQueryRegistry()
        {
            // Ordering invariant: Destroyed must fire BEFORE world.Unregister so
            // subscribers can run one last query while unwinding. Same contract
            // as MonoEntity.OnDestroy.
            var world = new World();
            var entity = new Entity(world);
            entity.Require<TestAspectA>();
            var visibleInsideHandler = false;

            entity.Destroyed += _ => visibleInsideHandler = world.QueryLocal<TestAspectA>().Any();
            entity.Dispose();

            Assert.IsTrue(visibleInsideHandler,
                "Destroyed handler must see the entity still in the registry — world.Unregister should run after the event fires.");
        }

        [Test]
        public void Ctor_WithoutWorld_Dispose_DoesNotTouchWorld()
        {
            // Backward-compat: entities created via the parameterless ctor keep
            // the manual-registration contract. Dispose must not silently drop
            // the entity from a world it was registered with externally.
            var world = new World();
            var entity = new Entity();
            entity.Require<TestAspectA>();
            world.Register(entity, typeof(TestAspectA));

            entity.Dispose();

            Assert.AreEqual(1, world.Registry.GetAllWith(typeof(TestAspectA)).Count,
                "no-world Entity must not auto-unregister — callers are still responsible for Unregister.");
        }

        [Test]
        public void Ctor_WithWorld_DisposeTwice_IsNoOp()
        {
            var world = new World();
            var entity = new Entity(world);
            entity.Require<TestAspectA>();
            var fireCount = 0;
            entity.Destroyed += _ => fireCount++;

            entity.Dispose();
            entity.Dispose();

            Assert.AreEqual(1, fireCount, "Destroyed must fire at most once.");
            Assert.IsEmpty(world.QueryLocal<TestAspectA>().ToList());
        }

        [Test]
        public void Ctor_WithWorld_ManualRegister_NoDoubleBucket()
        {
            // Defensive: if a caller mixes manual Register with auto-register
            // the registry's HashSet-based dedup keeps bucket size at 1.
            var world = new World();
            var entity = new Entity(world);

            world.Register(entity, typeof(TestAspectA));
            entity.Require<TestAspectA>();

            Assert.AreEqual(1, world.Registry.GetAllWith(typeof(TestAspectA)).Count);
        }

        private class TestAspectA : IEntityAspect { }
        private class TestAspectB : IEntityAspect { }
    }
}
