using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class WorldRestoreMigrationConsistencyTests
    {
        [PersistedKey("mig.player")]
        private sealed class PlayerAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
        }

        private sealed class FirstStepMigrator : IAspectSnapshotMigrator
        {
            public int FromFormatVersion => 0;
            public Action<AspectSnapshot> Body { get; set; } = _ => { };
            public int InvokeCount { get; private set; }

            public void Migrate(AspectSnapshot snap)
            {
                InvokeCount++;
                Body(snap);
            }
        }

        private sealed class SecondStepMigrator : IAspectSnapshotMigrator
        {
            public int FromFormatVersion => 1;
            public Action<AspectSnapshot> Body { get; set; } = _ => { };
            public int InvokeCount { get; private set; }

            public void Migrate(AspectSnapshot snap)
            {
                InvokeCount++;
                Body(snap);
            }
        }

        [SetUp]
        public void SetUp() => PersistedKeyRegistry.ResetForTests();

        [TearDown]
        public void TearDown() => PersistedKeyRegistry.ResetForTests();

        [Test]
        public void RestoreAll_SnapshotMigratorThrowsMidChain_FormatVersionReflectsPartialProgress()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("IAspectSnapshotMigrator.*threw"));

            var first = new FirstStepMigrator
            {
                // IAspectSnapshotMigrator.Migrate is invoked per entity snapshot — receives the
                // entity's AspectSnapshot, not the WorldSnapshot. Mutate the aspect data directly.
                Body = aspect => aspect.Aspects["mig.player"].Fields["Health"] = 50,
            };
            var second = new SecondStepMigrator
            {
                Body = _ => throw new InvalidOperationException("boom"), // fails
            };

            var migrations = new PersistenceMigrationRegistry()
                .AddSnapshot(first)
                .AddSnapshot(second);

            Assert.AreEqual(2, migrations.CurrentFormatVersion);

            var snap = new WorldSnapshot { FormatVersion = 0 };
            var data = new AspectData();
            data.Fields["Health"] = 10;
            var aspectSnap = new AspectSnapshot();
            aspectSnap.Aspects["mig.player"] = data;
            snap.Entities["e1"] = aspectSnap;

            var world = new World();
            world.RestoreAll(snap, _ => new Entity(world),
                new WorldRestoreOptions { Migrations = migrations });

            Assert.AreEqual(1, snap.FormatVersion,
                "FormatVersion must advance past every successful step so a retry doesn't re-run completed migrators.");
            Assert.AreEqual(1, first.InvokeCount);
            Assert.AreEqual(1, second.InvokeCount);
            Assert.AreEqual(50, snap.Entities["e1"].Aspects["mig.player"].Fields["Health"],
                "First migrator's mutation persists.");

            world.Dispose();
        }

        [Test]
        public void RestoreAll_SnapshotMigratorChainCompletes_FormatVersionMatchesTarget()
        {
            var first = new FirstStepMigrator();
            var second = new SecondStepMigrator();
            var migrations = new PersistenceMigrationRegistry()
                .AddSnapshot(first)
                .AddSnapshot(second);

            var snap = new WorldSnapshot { FormatVersion = 0 };
            var aspectSnap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Health"] = 10;
            aspectSnap.Aspects["mig.player"] = data;
            snap.Entities["e1"] = aspectSnap;

            var world = new World();
            world.RestoreAll(snap, _ => new Entity(world),
                new WorldRestoreOptions { Migrations = migrations });

            Assert.AreEqual(2, snap.FormatVersion);

            world.Dispose();
        }

        [Test]
        public void RestoreAll_ResolveOrSpawnReturnsWorld_LogsErrorAndSkips()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("resolveOrSpawn returned the World itself"));

            var snap = new WorldSnapshot();
            var aspectSnap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Health"] = 42;
            aspectSnap.Aspects["mig.player"] = data;
            snap.Entities["rogue-id"] = aspectSnap;

            var world = new World();

            Assert.DoesNotThrow(() => world.RestoreAll(snap, _ => world));

            // World-scoped restore path was not touched because snapshot.World is null; the per-entity
            // restore was skipped, so the world has no PlayerAspect.
            Assert.IsFalse(((IEntity)world).HasPersistedState(),
                "When resolveOrSpawn returns the world, the per-entity restore must be skipped " +
                "so the world is not restored twice with mismatched data.");

            world.Dispose();
        }
    }
}
