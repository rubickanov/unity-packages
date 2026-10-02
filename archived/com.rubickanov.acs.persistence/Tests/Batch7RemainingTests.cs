using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests.Persistence
{
    /// <summary>
    /// Tests 7.1, 7.3, 7.5, 7.6, 7.9, 7.11 from the persistence audit — cover the rougher edges
    /// that the earlier PR test files left out.
    /// </summary>
    [TestFixture]
    public class Batch7RemainingTests
    {
        [PersistedKey("batch7.throwing")]
        [PersistedVersion(1)]
        private sealed class ThrowingMigrationAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Value = new(0);
        }

        private sealed class ThrowingV0Migrator : IAspectMigrator
        {
            public string AspectKey => "batch7.throwing";
            public int FromVersion => 0;
            public void Migrate(AspectData data) => throw new InvalidOperationException("simulated migrator failure");
        }

        [Test]
        public void Restore_AspectMigratorThrows_LogsErrorAndSkipsAspect()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("simulated migrator failure"));

            var entity = new Entity();
            entity.Require<ThrowingMigrationAspect>().Value.Value = 99; // untouched after restore

            var snap = new AspectSnapshot();
            var data = new AspectData { Version = 0 };
            data.Fields["Value"] = 42;
            snap.Aspects["batch7.throwing"] = data;

            var registry = new PersistenceMigrationRegistry().AddAspect(new ThrowingV0Migrator());

            entity.Restore(snap, registry);

            // Migrator threw → aspect entry skipped, in-memory value stays at the pre-restore default.
            Assert.AreEqual(99, entity.Require<ThrowingMigrationAspect>().Value.Value);
        }

        private sealed class FloatAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<float> Damage = new(1.0f);
        }

        [Test]
        public void Restore_TypeMismatchIntIntoFloat_LogsAndSkipsField()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("type mismatch"));

            var entity = new Entity();
            entity.Require<FloatAspect>().Damage.Value = 5f;

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Damage"] = 7; // boxed int — (float) unboxing throws InvalidCastException
            snap.Aspects[typeof(FloatAspect).FullName] = data;

            entity.Restore(snap);

            // Field-level cast mismatch is isolated — current value survives.
            Assert.AreEqual(5f, entity.Require<FloatAspect>().Damage.Value);
        }

        private sealed class NullableAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int?> Score = new(null);
        }

        [Test]
        public void Snapshot_ReactivePropertyOfNullableInt_RoundTripsValue()
        {
            var source = new Entity();
            source.Require<NullableAspect>().Score.Value = 42;

            var snap = source.Snapshot();

            var target = new Entity();
            target.Restore(snap);

            Assert.AreEqual((int?)42, target.Require<NullableAspect>().Score.Value);
        }

        [Test]
        public void Snapshot_ReactivePropertyOfNullableInt_RoundTripsNull()
        {
            var source = new Entity();
            source.Require<NullableAspect>().Score.Value = null;

            var snap = source.Snapshot();

            var target = new Entity();
            target.Require<NullableAspect>().Score.Value = 100; // pre-seed, should be overwritten by null
            target.Restore(snap);

            Assert.IsNull(target.Require<NullableAspect>().Score.Value);
        }

        private sealed class MultiFieldAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Zebra = new(1);
            [PersistedState] public readonly ReactiveProperty<int> Apple = new(2);
            [PersistedState] public readonly ReactiveProperty<int> Mango = new(3);
        }

        [Test]
        public void Snapshot_SameStateTwice_ProducesIdenticalKeyOrder()
        {
            var entity = new Entity();
            var a = entity.Require<MultiFieldAspect>();
            a.Zebra.Value = 10;
            a.Apple.Value = 20;
            a.Mango.Value = 30;

            var first = entity.Snapshot();
            var second = entity.Snapshot();

            // SortedDictionary with StringComparer.Ordinal — iteration must be stable across calls.
            CollectionAssert.AreEqual(
                first.Aspects[typeof(MultiFieldAspect).FullName].Fields.Keys.ToArray(),
                second.Aspects[typeof(MultiFieldAspect).FullName].Fields.Keys.ToArray());

            CollectionAssert.AreEqual(
                new[] { "Apple", "Mango", "Zebra" },
                first.Aspects[typeof(MultiFieldAspect).FullName].Fields.Keys.ToArray(),
                "Ordinal ordering of field keys is part of the format determinism contract.");
        }

        private sealed class PersistedMarkerAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Value = new(0);
        }

        [Test]
        public void RestoreAll_CustomDisposeMissingCallback_InvokedForEachMissingEntity()
        {
            var world = new World();
            var a = new Entity(world);
            a.Require<PersistedMarkerAspect>().Value.Value = 1;
            var b = new Entity(world);
            b.Require<PersistedMarkerAspect>().Value.Value = 2;

            var disposed = new List<IEntity>();
            var emptySnap = new WorldSnapshot();

            world.RestoreAll(
                emptySnap,
                _ => null,
                new WorldRestoreOptions
                {
                    Missing = MissingEntityPolicy.DisposeMissing,
                    DisposeMissing = e => disposed.Add(e),
                });

            CollectionAssert.AreEquivalent(new[] { a, b }, disposed,
                "Custom DisposeMissing callback must fire exactly once per missing persisted entity.");

            world.Dispose();
        }

        private sealed class SetAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableHashSet<int> Tags = new();
        }

        private sealed class DictAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableDictionary<string, float> Cooldowns = new();
        }

        [Test]
        public void Restore_ObservableHashSet_FiresObserveAddForEachElement()
        {
            var source = new Entity();
            var src = source.Require<SetAspect>();
            src.Tags.Add(11);
            src.Tags.Add(22);
            var snap = source.Snapshot();

            var target = new Entity();
            var tags = target.Require<SetAspect>();
            var added = new List<int>();
            using var sub = tags.Tags.ObserveAdd().Subscribe(e => added.Add(e.Value));

            target.Restore(snap);

            CollectionAssert.AreEquivalent(new[] { 11, 22 }, added);
        }

        [Test]
        public void Restore_ObservableDictionary_FiresObserveAddForEachEntry()
        {
            var source = new Entity();
            var src = source.Require<DictAspect>();
            src.Cooldowns["fireball"] = 3f;
            src.Cooldowns["heal"] = 1.5f;
            var snap = source.Snapshot();

            var target = new Entity();
            var cd = target.Require<DictAspect>();
            var added = new List<KeyValuePair<string, float>>();
            using var sub = cd.Cooldowns.ObserveAdd().Subscribe(e => added.Add(e.Value));

            target.Restore(snap);

            Assert.AreEqual(2, added.Count);
            Assert.IsTrue(added.Any(p => p.Key == "fireball" && p.Value == 3f));
            Assert.IsTrue(added.Any(p => p.Key == "heal" && p.Value == 1.5f));
        }
    }
}
