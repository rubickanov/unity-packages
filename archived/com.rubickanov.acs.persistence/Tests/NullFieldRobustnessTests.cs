using System;
using System.Collections.Generic;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class NullFieldRobustnessTests
    {
        private sealed class UninitializedReactiveAspect : IEntityAspect
        {
            [PersistedState] public ReactiveProperty<int> Health;
        }

        private sealed class UninitializedListAspect : IEntityAspect
        {
            [PersistedState] public ObservableList<int> Items;
        }

        private sealed class HealthAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
        }

        private sealed class InventoryAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableList<int> Items = new();
        }

        private sealed class TagsAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableHashSet<int> Tags = new();
        }

        private sealed class CooldownsAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableDictionary<string, float> Cooldowns = new();
        }

        [Test]
        public void Snapshot_UninitializedReactiveField_ThrowsInvalidOperation()
        {
            var entity = new Entity();
            entity.Require<UninitializedReactiveAspect>();

            var ex = Assert.Throws<InvalidOperationException>(() => entity.Snapshot());
            StringAssert.Contains("[PersistedState]", ex.Message);
            StringAssert.Contains("null", ex.Message);
        }

        [Test]
        public void Snapshot_UninitializedCollectionField_ThrowsInvalidOperation()
        {
            var entity = new Entity();
            entity.Require<UninitializedListAspect>();

            var ex = Assert.Throws<InvalidOperationException>(() => entity.Snapshot());
            StringAssert.Contains("[PersistedState]", ex.Message);
            StringAssert.Contains("null", ex.Message);
        }

        [Test]
        public void Restore_NullValueIntoValueTypeReactiveProperty_LogsAndSkips()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("type mismatch"));

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields[nameof(HealthAspect.Health)] = null;
            snap.Aspects[typeof(HealthAspect).FullName] = data;

            var entity = new Entity();
            Assert.DoesNotThrow(() => entity.Restore(snap));

            // Default value survives — bad field was skipped, not poisoned the restore.
            Assert.AreEqual(100, entity.Require<HealthAspect>().Health.Value);
        }

        [Test]
        public void Restore_NullValueIntoReferenceTypeReactiveProperty_WritesNull()
        {
            var sourceAspect = typeof(EntitySnapshotTargetAspect);
            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields[nameof(EntitySnapshotTargetAspect.Name)] = null;
            snap.Aspects[sourceAspect.FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.IsNull(entity.Require<EntitySnapshotTargetAspect>().Name.Value,
                "Null is a legal value for ReactiveProperty<string> — restore must write it through.");
        }

        [Test]
        public void Restore_NullValueIntoObservableList_ClearsCollection()
        {
            var entity = new Entity();
            var inv = entity.Require<InventoryAspect>();
            inv.Items.Add(1);
            inv.Items.Add(2);

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields[nameof(InventoryAspect.Items)] = null;
            snap.Aspects[typeof(InventoryAspect).FullName] = data;

            entity.Restore(snap);

            Assert.AreEqual(0, inv.Items.Count,
                "Writing null into an ObservableList<T>-backed binding must clear the collection.");
        }

        [Test]
        public void Restore_NullValueIntoObservableHashSet_ClearsCollection()
        {
            var entity = new Entity();
            var tags = entity.Require<TagsAspect>();
            tags.Tags.Add(7);

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields[nameof(TagsAspect.Tags)] = null;
            snap.Aspects[typeof(TagsAspect).FullName] = data;

            entity.Restore(snap);

            Assert.AreEqual(0, tags.Tags.Count);
        }

        [Test]
        public void Restore_NullValueIntoObservableDictionary_ClearsCollection()
        {
            var entity = new Entity();
            var cd = entity.Require<CooldownsAspect>();
            cd.Cooldowns["dash"] = 2f;

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields[nameof(CooldownsAspect.Cooldowns)] = null;
            snap.Aspects[typeof(CooldownsAspect).FullName] = data;

            entity.Restore(snap);

            Assert.AreEqual(0, cd.Cooldowns.Count);
        }

        [Test]
        public void Restore_OneBadFieldDoesNotPoisonOtherFields()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("type mismatch"));

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields[nameof(EntitySnapshotTargetAspect.Health)] = null; // value-type, should be skipped
            data.Fields[nameof(EntitySnapshotTargetAspect.Name)] = "Morgana"; // should still be applied
            snap.Aspects[typeof(EntitySnapshotTargetAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            var aspect = entity.Require<EntitySnapshotTargetAspect>();
            Assert.AreEqual(50, aspect.Health.Value, "Bad field keeps default.");
            Assert.AreEqual("Morgana", aspect.Name.Value, "Good field was applied after bad one was skipped.");
        }

        private sealed class EntitySnapshotTargetAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(50);
            [PersistedState] public readonly ReactiveProperty<string> Name = new("unset");
        }
    }
}
