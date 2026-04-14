using System.Linq;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class WorldPersistenceTests
    {
        private sealed class PersistedAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Value = new(0);
        }

        private sealed class RuntimeOnlyAspect : IEntityAspect
        {
            public readonly ReactiveProperty<int> Ticks = new(0);
        }

        private sealed class WorldTimeAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<float> TimeOfDay = new(0f);
        }

        [Test]
        public void PersistedEntities_EmptyWorld_ReturnsOnlyWorldIfItHasPersistedState()
        {
            var world = new World();

            var result = world.PersistedEntities().ToArray();

            // World is self-registered but has no aspects yet → filtered out.
            Assert.IsEmpty(result);

            world.Dispose();
        }

        [Test]
        public void PersistedEntities_EntityWithoutPersistedAspects_IsFilteredOut()
        {
            var world = new World();
            var entity = new Entity(world);
            entity.Require<RuntimeOnlyAspect>();

            var result = world.PersistedEntities().ToArray();

            CollectionAssert.DoesNotContain(result, entity);

            world.Dispose();
        }

        [Test]
        public void PersistedEntities_EntityWithPersistedAspect_IsIncluded()
        {
            var world = new World();
            var entity = new Entity(world);
            entity.Require<PersistedAspect>().Value.Value = 42;

            var result = world.PersistedEntities().ToArray();

            CollectionAssert.Contains(result, entity);

            world.Dispose();
        }

        [Test]
        public void PersistedEntities_WorldWithPersistedAspect_IsIncludedItself()
        {
            var world = new World();
            ((IEntity)world).Require<WorldTimeAspect>().TimeOfDay.Value = 12.5f;

            var result = world.PersistedEntities().ToArray();

            CollectionAssert.Contains(result, world);

            world.Dispose();
        }

        [Test]
        public void Snapshot_WorldAspect_RoundTripsThroughWorldAsIEntity()
        {
            var world = new World();
            ((IEntity)world).Require<WorldTimeAspect>().TimeOfDay.Value = 8f;

            IEntity asEntity = world;
            var snap = asEntity.Snapshot();

            var restored = new World();
            IEntity restoredEntity = restored;
            restoredEntity.Restore(snap);

            Assert.AreEqual(8f, ((IEntity)restored).Require<WorldTimeAspect>().TimeOfDay.Value);

            world.Dispose();
            restored.Dispose();
        }
    }
}
