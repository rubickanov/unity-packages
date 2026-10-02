using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class PersistenceVersioningTests
    {
        [PersistedKey("ver.keyed")]
        private sealed class KeyedAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
        }

        [PersistedAlias("ver.legacy.aliased")]
        private sealed class AliasedAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
        }

        private sealed class PlainAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
        }

        [PersistedKey("ver.versioned")]
        [PersistedVersion(1)]
        private sealed class VersionedAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(0);
        }

        [PersistedKey("ver.versioned.v2")]
        [PersistedVersion(2)]
        private sealed class VersionedAspectV2 : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(0);
            [PersistedState] public readonly ReactiveProperty<int> Mana = new(0);
        }

        // Snapshot a stored version 3, code target version 0 — downgrade path.
        private sealed class NoVersionAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(0);
        }

        [PersistedKey("ver.hero")]
        [PersistedVersion(1)]
        private sealed class HeroMigrationAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(0);
            [PersistedState] public readonly ReactiveProperty<int> Level = new(1);
            [PersistedState] public readonly ReactiveProperty<int> ManaMax = new(0);
        }

        [PersistedKey("ver.split.health")]
        private sealed class SplitHealthAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Value = new(0);
        }

        [PersistedKey("ver.split.shield")]
        private sealed class SplitShieldAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Value = new(0);
        }

        private sealed class HeroMigratorV0ToV1 : IAspectMigrator
        {
            public string AspectKey => "ver.hero";
            public int FromVersion => 0;

            public void Migrate(AspectData data)
            {
                // rename HP → Health
                if (data.Fields.TryGetValue("HP", out var hp))
                {
                    data.Fields["Health"] = hp;
                    data.Fields.Remove("HP");
                }
                // compute default for ManaMax from Level
                var level = data.Fields.TryGetValue("Level", out var lv) ? (int)lv : 1;
                data.Fields["ManaMax"] = level * 10;
            }
        }

        private sealed class VersionedV0ToV1 : IAspectMigrator
        {
            public string AspectKey => "ver.versioned.v2";
            public int FromVersion => 0;
            public void Migrate(AspectData data) { data.Fields["Health"] = 10; }
        }

        private sealed class VersionedV1ToV2 : IAspectMigrator
        {
            public string AspectKey => "ver.versioned.v2";
            public int FromVersion => 1;
            public void Migrate(AspectData data) { data.Fields["Mana"] = 20; }
        }

        private sealed class SplitHealthShieldMigrator : IAspectSnapshotMigrator
        {
            public int FromFormatVersion => 0;

            public void Migrate(AspectSnapshot snap)
            {
                if (!snap.Aspects.TryGetValue("legacy.healthshield", out var legacy)) return;
                var health = new AspectData();
                if (legacy.Fields.TryGetValue("Health", out var h)) health.Fields["Value"] = h;
                snap.Aspects["ver.split.health"] = health;

                var shield = new AspectData();
                if (legacy.Fields.TryGetValue("Shield", out var s)) shield.Fields["Value"] = s;
                snap.Aspects["ver.split.shield"] = shield;

                snap.Aspects.Remove("legacy.healthshield");
            }
        }

        private sealed class DeleteObsoleteMigrator : IAspectSnapshotMigrator
        {
            public int FromFormatVersion => 0;
            public void Migrate(AspectSnapshot snap) { snap.Aspects.Remove("ver.split.shield"); }
        }

        [SetUp]
        public void SetUp()
        {
            PersistedKeyRegistry.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            // Collision tests seed a hand-built reverse index; make sure the next fixture
            // sees the real AppDomain-scanned one.
            PersistedKeyRegistry.ResetForTests();
        }

        // ───── stable key / alias ─────

        [Test]
        public void Snapshot_AspectWithPersistedKey_UsesKeyInsteadOfFullName()
        {
            var entity = new Entity();
            entity.Require<KeyedAspect>().Health.Value = 42;

            var snap = entity.Snapshot();

            Assert.IsTrue(snap.Aspects.ContainsKey("ver.keyed"));
            Assert.IsFalse(snap.Aspects.ContainsKey(typeof(KeyedAspect).FullName));
        }

        [Test]
        public void Snapshot_AspectWithoutPersistedKey_UsesFullNameAsFallback()
        {
            var entity = new Entity();
            entity.Require<PlainAspect>().Health.Value = 7;

            var snap = entity.Snapshot();

            Assert.IsTrue(snap.Aspects.ContainsKey(typeof(PlainAspect).FullName));
        }

        [Test]
        public void Restore_AspectWithPersistedKey_ResolvesByStableKey()
        {
            var source = new Entity();
            source.Require<KeyedAspect>().Health.Value = 73;
            var snap = source.Snapshot();

            var target = new Entity();
            target.Restore(snap);

            Assert.AreEqual(73, target.Require<KeyedAspect>().Health.Value);
        }

        [Test]
        public void Restore_AspectWithAlias_ResolvesOldKeyToNewType()
        {
            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields[nameof(AliasedAspect.Health)] = 55;
            snap.Aspects["ver.legacy.aliased"] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(55, entity.Require<AliasedAspect>().Health.Value);
        }

        [Test]
        public void Restore_UnknownAspectKey_LogsWarningAndSkipsRest()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("acs.persistence.*not found"));

            var snap = new AspectSnapshot();
            snap.Aspects["no.such.aspect.anywhere"] = new AspectData();
            var data = new AspectData();
            data.Fields[nameof(KeyedAspect.Health)] = 9;
            snap.Aspects["ver.keyed"] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(9, entity.Require<KeyedAspect>().Health.Value);
        }

        [Test]
        public void Registry_DuplicatePersistedKey_LogsErrorAtRegister()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("PersistedKeyRegistry.*already registered"));

            PersistedKeyRegistry.TestOnly_SeedEmptyReverseIndex();
            PersistedKeyRegistry.TestOnly_Register("collide.me", typeof(KeyedAspect), "[PersistedKey]");
            PersistedKeyRegistry.TestOnly_Register("collide.me", typeof(AliasedAspect), "[PersistedKey]");
        }

        // ───── per-aspect versioning ─────

        [Test]
        public void Snapshot_AspectWithPersistedVersion_WritesVersionIntoAspectData()
        {
            var entity = new Entity();
            entity.Require<VersionedAspect>().Health.Value = 1;

            var snap = entity.Snapshot();

            Assert.AreEqual(1, snap.Aspects["ver.versioned"].Version);
        }

        [Test]
        public void Snapshot_AspectWithoutPersistedVersion_WritesZeroVersion()
        {
            var entity = new Entity();
            entity.Require<PlainAspect>().Health.Value = 1;

            var snap = entity.Snapshot();

            Assert.AreEqual(0, snap.Aspects[typeof(PlainAspect).FullName].Version);
        }

        [Test]
        public void Restore_VersionMatches_SkipsAspectMigration()
        {
            var snap = new AspectSnapshot();
            var data = new AspectData { Version = 1 };
            data.Fields[nameof(VersionedAspect.Health)] = 99;
            snap.Aspects["ver.versioned"] = data;

            var entity = new Entity();
            // null registry — version matches, no migration needed, should still restore cleanly.
            entity.Restore(snap, registry: null);

            Assert.AreEqual(99, entity.Require<VersionedAspect>().Health.Value);
        }

        [Test]
        public void Restore_VersionLowerAndChainRegistered_AppliesChainSequentially()
        {
            var snap = new AspectSnapshot();
            var data = new AspectData { Version = 0 };
            snap.Aspects["ver.versioned.v2"] = data;

            var registry = new PersistenceMigrationRegistry()
                .AddAspect(new VersionedV0ToV1())
                .AddAspect(new VersionedV1ToV2());

            var entity = new Entity();
            entity.Restore(snap, registry);

            var restored = entity.Require<VersionedAspectV2>();
            Assert.AreEqual(10, restored.Health.Value, "v0→v1 migrator sets Health = 10");
            Assert.AreEqual(20, restored.Mana.Value, "v1→v2 migrator sets Mana = 20");
            Assert.AreEqual(2, data.Version, "Registry advances AspectData.Version after each step");
        }

        [Test]
        public void Restore_VersionLowerAndNoRegistry_WarnsAndSkips()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("needs migration from version 0 to 1"));

            var snap = new AspectSnapshot();
            var data = new AspectData { Version = 0 };
            data.Fields[nameof(VersionedAspect.Health)] = 50;
            snap.Aspects["ver.versioned"] = data;

            var entity = new Entity();
            entity.Restore(snap, registry: null);

            // Aspect not created at all — skipped before Require<T>.
            Assert.AreEqual(0, entity.Require<VersionedAspect>().Health.Value, "Default kept when migration skipped.");
        }

        [Test]
        public void Restore_VersionLowerAndChainIncomplete_WarnsAndSkips()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("needs migration from version 0 to 2"));

            var snap = new AspectSnapshot();
            var data = new AspectData { Version = 0 };
            snap.Aspects["ver.versioned.v2"] = data;

            // Only v0→v1 registered, missing v1→v2.
            var registry = new PersistenceMigrationRegistry().AddAspect(new VersionedV0ToV1());

            var entity = new Entity();
            entity.Restore(snap, registry);

            Assert.AreEqual(0, entity.Require<VersionedAspectV2>().Health.Value, "Skipped — aspect not created.");
        }

        [Test]
        public void Restore_VersionHigherThanTarget_WarnsAndSkips()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("newer than current .PersistedVersion"));

            var snap = new AspectSnapshot();
            var data = new AspectData { Version = 5 };
            data.Fields[nameof(NoVersionAspect.Health)] = 50;
            snap.Aspects[typeof(NoVersionAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(0, entity.Require<NoVersionAspect>().Health.Value, "Downgrade refused.");
        }

        [Test]
        public void Registry_AddDuplicateAspectMigrator_Throws()
        {
            var registry = new PersistenceMigrationRegistry().AddAspect(new VersionedV0ToV1());

            Assert.Throws<InvalidOperationException>(() => registry.AddAspect(new VersionedV0ToV1()));
        }

        [Test]
        public void Restore_AspectMigratorMutatesFields_WrittenValuesReflectMigration()
        {
            var snap = new AspectSnapshot();
            var data = new AspectData { Version = 0 };
            data.Fields["HP"] = 77;       // old field name
            data.Fields["Level"] = 5;      // still present
            snap.Aspects["ver.hero"] = data;

            var registry = new PersistenceMigrationRegistry().AddAspect(new HeroMigratorV0ToV1());

            var entity = new Entity();
            entity.Restore(snap, registry);

            var hero = entity.Require<HeroMigrationAspect>();
            Assert.AreEqual(77, hero.Health.Value);
            Assert.AreEqual(5, hero.Level.Value);
            Assert.AreEqual(50, hero.ManaMax.Value);
        }

        // ───── snapshot-level (cross-aspect) ─────

        [Test]
        public void SnapshotAll_WithRegistry_WritesCurrentFormatVersion()
        {
            var world = new World();
            var registry = new PersistenceMigrationRegistry()
                .AddSnapshot(new SplitHealthShieldMigrator()); // FromFormatVersion 0 → 1

            var snap = world.SnapshotAll(_ => "x", registry);

            Assert.AreEqual(1, snap.FormatVersion);

            world.Dispose();
        }

        [Test]
        public void SnapshotAll_WithoutRegistry_WritesZeroFormatVersion()
        {
            var world = new World();

            var snap = world.SnapshotAll(_ => "x");

            Assert.AreEqual(0, snap.FormatVersion);

            world.Dispose();
        }

        [Test]
        public void RestoreAll_SnapshotMigratorSplitsAspect_ResultingAspectsRestored()
        {
            var snap = new WorldSnapshot { FormatVersion = 0 };
            var entitySnap = new AspectSnapshot();
            var legacy = new AspectData();
            legacy.Fields["Health"] = 80;
            legacy.Fields["Shield"] = 30;
            entitySnap.Aspects["legacy.healthshield"] = legacy;
            snap.Entities["e1"] = entitySnap;

            var registry = new PersistenceMigrationRegistry().AddSnapshot(new SplitHealthShieldMigrator());

            var world = new World();
            var target = new Entity(world);
            world.RestoreAll(snap, _ => target, new WorldRestoreOptions { Migrations = registry });

            Assert.AreEqual(80, target.Require<SplitHealthAspect>().Value.Value);
            Assert.AreEqual(30, target.Require<SplitShieldAspect>().Value.Value);
            Assert.AreEqual(1, snap.FormatVersion, "Registry advances snapshot FormatVersion on success.");

            world.Dispose();
        }

        [Test]
        public void RestoreAll_SnapshotMigratorDeletesAspect_TargetAspectSkipped()
        {
            var snap = new WorldSnapshot { FormatVersion = 0 };
            var entitySnap = new AspectSnapshot();
            var health = new AspectData();
            health.Fields["Value"] = 11;
            entitySnap.Aspects["ver.split.health"] = health;
            var shield = new AspectData();
            shield.Fields["Value"] = 22;
            entitySnap.Aspects["ver.split.shield"] = shield;
            snap.Entities["e1"] = entitySnap;

            var registry = new PersistenceMigrationRegistry().AddSnapshot(new DeleteObsoleteMigrator());

            var world = new World();
            var target = new Entity(world);
            world.RestoreAll(snap, _ => target, new WorldRestoreOptions { Migrations = registry });

            Assert.AreEqual(11, target.Require<SplitHealthAspect>().Value.Value);
            Assert.AreEqual(0, target.Require<SplitShieldAspect>().Value.Value, "Shield aspect was deleted from snapshot.");

            world.Dispose();
        }

        [Test]
        public void RestoreAll_FormatVersionMatchesCurrent_SkipsSnapshotMigration()
        {
            var snap = new WorldSnapshot { FormatVersion = 1 };
            var entitySnap = new AspectSnapshot();
            var health = new AspectData();
            health.Fields["Value"] = 42;
            entitySnap.Aspects["ver.split.health"] = health;
            snap.Entities["e1"] = entitySnap;

            var registry = new PersistenceMigrationRegistry().AddSnapshot(new SplitHealthShieldMigrator());

            var world = new World();
            var target = new Entity(world);
            world.RestoreAll(snap, _ => target, new WorldRestoreOptions { Migrations = registry });

            Assert.AreEqual(42, target.Require<SplitHealthAspect>().Value.Value, "No split needed — value preserved.");

            world.Dispose();
        }

        [Test]
        public void RestoreAll_FormatVersionGap_WarnsAndSkipsSnapshotMigrations()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("no complete IAspectSnapshotMigrator chain"));

            var snap = new WorldSnapshot { FormatVersion = 0 };
            var entitySnap = new AspectSnapshot();
            var health = new AspectData();
            health.Fields["Value"] = 99;
            entitySnap.Aspects["ver.split.health"] = health;
            snap.Entities["e1"] = entitySnap;

            // Registry targets format version 2 (two snapshot migrators), but only step 1→2 is registered.
            var registry = new PersistenceMigrationRegistry();
            registry.AddSnapshot(new NoOpSnapshotMigrator1To2());

            var world = new World();
            var target = new Entity(world);
            world.RestoreAll(snap, _ => target, new WorldRestoreOptions { Migrations = registry });

            // Per-aspect restore still runs — proves "snapshot migrations skipped, the rest keeps going".
            Assert.AreEqual(99, target.Require<SplitHealthAspect>().Value.Value);

            world.Dispose();
        }

        private sealed class NoOpSnapshotMigrator1To2 : IAspectSnapshotMigrator
        {
            public int FromFormatVersion => 1;
            public void Migrate(AspectSnapshot snapshot) { }
        }

        [Test]
        public void RestoreAll_SnapshotMigratorAppliedToWorldAspectSnapshotToo()
        {
            var snap = new WorldSnapshot { FormatVersion = 0 };
            var worldSnap = new AspectSnapshot();
            var legacy = new AspectData();
            legacy.Fields["Health"] = 7;
            legacy.Fields["Shield"] = 3;
            worldSnap.Aspects["legacy.healthshield"] = legacy;
            snap.World = worldSnap;

            var registry = new PersistenceMigrationRegistry().AddSnapshot(new SplitHealthShieldMigrator());

            var world = new World();
            world.RestoreAll(snap, _ => null, new WorldRestoreOptions { Migrations = registry });

            Assert.AreEqual(7, ((IEntity)world).Require<SplitHealthAspect>().Value.Value);
            Assert.AreEqual(3, ((IEntity)world).Require<SplitShieldAspect>().Value.Value);

            world.Dispose();
        }

        [Test]
        public void Registry_AddDuplicateSnapshotMigrator_Throws()
        {
            var registry = new PersistenceMigrationRegistry().AddSnapshot(new SplitHealthShieldMigrator());

            Assert.Throws<InvalidOperationException>(() => registry.AddSnapshot(new SplitHealthShieldMigrator()));
        }

        // ───── integration / regression ─────

        [Test]
        public void Restore_OldSnapshotWithoutVersionField_TreatedAsVersionZero()
        {
            // Snapshot produced by pre-1.2 code (or by a saver that doesn't write Version) — Version defaults to 0.
            var snap = new AspectSnapshot();
            var data = new AspectData(); // Version == 0 by default
            data.Fields[nameof(PlainAspect.Health)] = 33;
            snap.Aspects[typeof(PlainAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap); // no registry, no migration needed — PlainAspect target is version 0

            Assert.AreEqual(33, entity.Require<PlainAspect>().Health.Value);
        }

        [Test]
        public void WorldSnapshot_FormatVersionRoundTrip_Preserved()
        {
            var snap = new WorldSnapshot { FormatVersion = 42 };

            Assert.AreEqual(42, snap.FormatVersion);
        }
    }
}
