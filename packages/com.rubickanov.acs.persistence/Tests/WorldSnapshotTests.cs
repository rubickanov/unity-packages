using System;
using System.Collections.Generic;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class WorldSnapshotTests
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
        public void SnapshotAll_WorldWithoutPersistedAspects_WorldFieldIsNull()
        {
            var world = new World();
            var entity = new Entity(world);
            entity.Require<PersistedAspect>().Value.Value = 1;

            var snap = world.SnapshotAll(_ => "e1");

            Assert.IsNull(snap.World);

            world.Dispose();
        }

        [Test]
        public void SnapshotAll_WorldWithWorldTimeAspect_WorldFieldContainsIt()
        {
            var world = new World();
            ((IEntity)world).Require<WorldTimeAspect>().TimeOfDay.Value = 12.5f;

            var snap = world.SnapshotAll(_ => "irrelevant");

            Assert.IsNotNull(snap.World);
            Assert.IsTrue(snap.World.Aspects.ContainsKey(typeof(WorldTimeAspect).FullName));
            Assert.AreEqual(12.5f,
                snap.World.Aspects[typeof(WorldTimeAspect).FullName].Fields[nameof(WorldTimeAspect.TimeOfDay)]);

            world.Dispose();
        }

        [Test]
        public void SnapshotAll_TwoPersistedEntities_EntitiesMapContainsBothByKey()
        {
            var world = new World();
            var a = new Entity(world);
            var b = new Entity(world);
            a.Require<PersistedAspect>().Value.Value = 7;
            b.Require<PersistedAspect>().Value.Value = 9;

            var snap = world.SnapshotAll(e => ReferenceEquals(e, a) ? "A" : "B");

            Assert.AreEqual(2, snap.Entities.Count);
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, snap.Entities.Keys);

            world.Dispose();
        }

        [Test]
        public void SnapshotAll_RuntimeOnlyEntity_FilteredOut()
        {
            var world = new World();
            var runtime = new Entity(world);
            runtime.Require<RuntimeOnlyAspect>();
            var persisted = new Entity(world);
            persisted.Require<PersistedAspect>().Value.Value = 3;

            var snap = world.SnapshotAll(_ => "only-persisted");

            Assert.AreEqual(1, snap.Entities.Count);
            Assert.IsTrue(snap.Entities.ContainsKey("only-persisted"));

            world.Dispose();
        }

        [Test]
        public void SnapshotAll_DuplicateKeyFromKeyOf_ThrowsInvalidOperationException()
        {
            var world = new World();
            var a = new Entity(world);
            var b = new Entity(world);
            a.Require<PersistedAspect>().Value.Value = 1;
            b.Require<PersistedAspect>().Value.Value = 2;

            Assert.Throws<InvalidOperationException>(() => world.SnapshotAll(_ => "collide"));

            world.Dispose();
        }

        [Test]
        public void SnapshotAll_NullKeyFromKeyOf_ThrowsArgumentException()
        {
            var world = new World();
            var e = new Entity(world);
            e.Require<PersistedAspect>().Value.Value = 1;

            Assert.Throws<ArgumentException>(() => world.SnapshotAll(_ => null));

            world.Dispose();
        }

        [Test]
        public void RestoreAll_SnapshotWithWorldAspect_RestoresWorldState()
        {
            var source = new World();
            ((IEntity)source).Require<WorldTimeAspect>().TimeOfDay.Value = 6f;
            var snap = source.SnapshotAll(_ => "x");

            var target = new World();
            target.RestoreAll(snap, _ => null);

            Assert.AreEqual(6f, ((IEntity)target).Require<WorldTimeAspect>().TimeOfDay.Value);

            source.Dispose();
            target.Dispose();
        }

        [Test]
        public void RestoreAll_SnapshotEntities_CallsResolveOrSpawnAndRestores()
        {
            var source = new World();
            var src = new Entity(source);
            src.Require<PersistedAspect>().Value.Value = 42;
            var snap = source.SnapshotAll(_ => "hero");

            var target = new World();
            var spawned = new Entity(target);
            var calls = new List<string>();
            target.RestoreAll(snap, key =>
            {
                calls.Add(key);
                return spawned;
            });

            CollectionAssert.AreEqual(new[] { "hero" }, calls);
            Assert.AreEqual(42, spawned.Require<PersistedAspect>().Value.Value);

            source.Dispose();
            target.Dispose();
        }

        [Test]
        public void RestoreAll_ResolveOrSpawnReturnsNull_LogsWarningAndContinues()
        {
            var source = new World();
            var src = new Entity(source);
            src.Require<PersistedAspect>().Value.Value = 1;
            var snap = source.SnapshotAll(_ => "ghost");

            var target = new World();
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex(".*resolveOrSpawn returned null.*"));

            Assert.DoesNotThrow(() => target.RestoreAll(snap, _ => null));

            source.Dispose();
            target.Dispose();
        }

        [Test]
        public void RestoreAll_MissingEntityIgnoreDefault_LeavesLiveEntityAlone()
        {
            var world = new World();
            var live = new Entity(world);
            live.Require<PersistedAspect>().Value.Value = 5;

            var emptySnap = new WorldSnapshot();
            world.RestoreAll(emptySnap, _ => null);

            // live entity still registered, state intact.
            CollectionAssert.Contains(world.PersistedEntities(), live);
            Assert.AreEqual(5, live.Require<PersistedAspect>().Value.Value);

            world.Dispose();
        }

        [Test]
        public void RestoreAll_MissingEntityDispose_DisposesLiveEntityNotInSnapshot()
        {
            var world = new World();
            var live = new Entity(world);
            live.Require<PersistedAspect>().Value.Value = 5;

            var wasDisposed = false;
            live.Destroyed += _ => wasDisposed = true;

            var emptySnap = new WorldSnapshot();
            world.RestoreAll(
                emptySnap,
                _ => null,
                new WorldRestoreOptions { Missing = MissingEntityPolicy.DisposeMissing });

            Assert.IsTrue(wasDisposed);
            CollectionAssert.DoesNotContain(world.PersistedEntities(), live);

            world.Dispose();
        }

        [Test]
        public void RestoreAll_MissingEntityDispose_NeverDisposesWorld()
        {
            var world = new World();
            ((IEntity)world).Require<WorldTimeAspect>().TimeOfDay.Value = 3f;

            var worldDisposed = false;
            ((IEntity)world).Destroyed += _ => worldDisposed = true;

            // Snapshot without world-scoped aspects — World.World == null — and a DisposeMissing run.
            var emptySnap = new WorldSnapshot();
            world.RestoreAll(
                emptySnap,
                _ => null,
                new WorldRestoreOptions { Missing = MissingEntityPolicy.DisposeMissing });

            Assert.IsFalse(worldDisposed);

            world.Dispose();
        }

        [Test]
        public void RestoreAll_MissingEntityDispose_KeepsRuntimeOnlyEntity()
        {
            var world = new World();
            var runtime = new Entity(world);
            runtime.Require<RuntimeOnlyAspect>();

            var wasDisposed = false;
            runtime.Destroyed += _ => wasDisposed = true;

            var emptySnap = new WorldSnapshot();
            world.RestoreAll(
                emptySnap,
                _ => null,
                new WorldRestoreOptions { Missing = MissingEntityPolicy.DisposeMissing });

            Assert.IsFalse(wasDisposed);

            world.Dispose();
        }

        [Test]
        public void SnapshotAll_RoundTripThroughRestoreAll_EntityStatesMatch()
        {
            var source = new World();
            ((IEntity)source).Require<WorldTimeAspect>().TimeOfDay.Value = 11f;
            var a = new Entity(source);
            var b = new Entity(source);
            a.Require<PersistedAspect>().Value.Value = 10;
            b.Require<PersistedAspect>().Value.Value = 20;

            var srcMap = new Dictionary<IEntity, string> { [a] = "A", [b] = "B" };
            var snap = source.SnapshotAll(e => srcMap[e]);

            var target = new World();
            var ta = new Entity(target);
            var tb = new Entity(target);
            var resolver = new Dictionary<string, IEntity> { ["A"] = ta, ["B"] = tb };
            target.RestoreAll(snap, k => resolver[k]);

            Assert.AreEqual(11f, ((IEntity)target).Require<WorldTimeAspect>().TimeOfDay.Value);
            Assert.AreEqual(10, ta.Require<PersistedAspect>().Value.Value);
            Assert.AreEqual(20, tb.Require<PersistedAspect>().Value.Value);

            source.Dispose();
            target.Dispose();
        }
    }
}
