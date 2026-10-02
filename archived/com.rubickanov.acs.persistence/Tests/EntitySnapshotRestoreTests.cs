using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class EntitySnapshotRestoreTests
    {
        private sealed class PlayerAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
            [PersistedState] public readonly ReactiveProperty<string> Name = new("unset");
            public readonly ReactiveProperty<bool> IsInCombat = new(false);
        }

        private sealed class PositionAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<UnityEngine.Vector3> Position = new();
        }

        private sealed class RuntimeOnlyAspect : IEntityAspect
        {
            public readonly ReactiveProperty<int> Ticks = new(0);
        }

        [Test]
        public void Snapshot_ReactivePropertyInt_CapturesValue()
        {
            var entity = new Entity();
            entity.Require<PlayerAspect>().Health.Value = 73;

            var snap = entity.Snapshot();

            var key = typeof(PlayerAspect).FullName;
            Assert.IsTrue(snap.Aspects.ContainsKey(key));
            Assert.AreEqual(73, snap.Aspects[key].Fields[nameof(PlayerAspect.Health)]);
        }

        [Test]
        public void Snapshot_ReactivePropertyString_CapturesValue()
        {
            var entity = new Entity();
            entity.Require<PlayerAspect>().Name.Value = "Arthur";

            var snap = entity.Snapshot();

            Assert.AreEqual("Arthur",
                snap.Aspects[typeof(PlayerAspect).FullName].Fields[nameof(PlayerAspect.Name)]);
        }

        [Test]
        public void Snapshot_MultipleAspects_CapturesAllPersistedFields()
        {
            var entity = new Entity();
            entity.Require<PlayerAspect>().Health.Value = 42;
            entity.Require<PositionAspect>().Position.Value = new UnityEngine.Vector3(1, 2, 3);

            var snap = entity.Snapshot();

            Assert.IsTrue(snap.Aspects.ContainsKey(typeof(PlayerAspect).FullName));
            Assert.IsTrue(snap.Aspects.ContainsKey(typeof(PositionAspect).FullName));
        }

        [Test]
        public void Snapshot_MultipleAspectsAndFields_EnumerateInOrdinalSortedOrder()
        {
            var entity = new Entity();
            // Insertion happens in Position-first, Player-second order from the caller's POV.
            entity.Require<PositionAspect>().Position.Value = new UnityEngine.Vector3(1, 2, 3);
            entity.Require<PlayerAspect>().Health.Value = 7;

            var snap = entity.Snapshot();

            // Aspect keys (Type.FullName) — 'P...Player...' sorts before 'P...Position...' by ordinal.
            var aspectKeys = new System.Collections.Generic.List<string>(snap.Aspects.Keys);
            var expectedAspects = new System.Collections.Generic.List<string>(aspectKeys);
            expectedAspects.Sort(System.StringComparer.Ordinal);
            CollectionAssert.AreEqual(expectedAspects, aspectKeys,
                "AspectSnapshot.Aspects must enumerate in ordinal-sorted key order (determinism guarantee).");

            // Field keys within PlayerAspect — 'Health' before 'Name'.
            var playerFields = new System.Collections.Generic.List<string>(
                snap.Aspects[typeof(PlayerAspect).FullName].Fields.Keys);
            CollectionAssert.AreEqual(new[] { nameof(PlayerAspect.Health), nameof(PlayerAspect.Name) }, playerFields);
        }

        [Test]
        public void Snapshot_AspectWithoutPersistedFields_IsOmitted()
        {
            var entity = new Entity();
            entity.Require<RuntimeOnlyAspect>().Ticks.Value = 99;

            var snap = entity.Snapshot();

            Assert.IsFalse(snap.Aspects.ContainsKey(typeof(RuntimeOnlyAspect).FullName));
            Assert.IsTrue(snap.IsEmpty);
        }

        [Test]
        public void Restore_IntoFreshEntity_WritesValuesBack()
        {
            var source = new Entity();
            var p = source.Require<PlayerAspect>();
            p.Health.Value = 55;
            p.Name.Value = "Morgana";
            var snap = source.Snapshot();

            var target = new Entity();
            target.Restore(snap);

            var restored = target.Require<PlayerAspect>();
            Assert.AreEqual(55, restored.Health.Value);
            Assert.AreEqual("Morgana", restored.Name.Value);
        }

        [Test]
        public void Restore_TriggersReactivePropertySubscriber()
        {
            var source = new Entity();
            source.Require<PlayerAspect>().Health.Value = 31;
            var snap = source.Snapshot();

            var target = new Entity();
            int seen = -1;
            // Subscribe AFTER Require so we don't catch the initial default-value emission.
            var aspect = target.Require<PlayerAspect>();
            using var sub = aspect.Health.Skip(1).Subscribe(v => seen = v);

            target.Restore(snap);

            Assert.AreEqual(31, seen, "Restore must look like a normal write — subscribers should observe it.");
        }

        [Test]
        public void Restore_SnapshotWithoutField_LeavesAspectDefault()
        {
            var snap = new AspectSnapshot();
            snap.Aspects[typeof(PlayerAspect).FullName] = new AspectData();
            // No fields populated.

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(100, entity.Require<PlayerAspect>().Health.Value);
            Assert.AreEqual("unset", entity.Require<PlayerAspect>().Name.Value);
        }

        [Test]
        public void Restore_UnknownFieldName_IsSilentlyIgnored()
        {
            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["FieldThatNoLongerExists"] = 999;
            data.Fields[nameof(PlayerAspect.Health)] = 77;
            snap.Aspects[typeof(PlayerAspect).FullName] = data;

            var entity = new Entity();
            Assert.DoesNotThrow(() => entity.Restore(snap));

            Assert.AreEqual(77, entity.Require<PlayerAspect>().Health.Value);
        }

        [Test]
        public void Restore_UnknownAspectType_LogsWarningAndSkipsRest()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("acs.persistence.*not found"));

            var snap = new AspectSnapshot();
            snap.Aspects["Rubickanov.NoSuchNamespace.PhantomAspect"] = new AspectData();
            // Include a real aspect AFTER the phantom to verify the loop keeps going.
            var data = new AspectData();
            data.Fields[nameof(PlayerAspect.Health)] = 44;
            snap.Aspects[typeof(PlayerAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(44, entity.Require<PlayerAspect>().Health.Value);
        }

        [Test]
        public void HasPersistedState_AspectWithPersistedField_ReturnsTrue()
        {
            var entity = new Entity();
            entity.Require<PlayerAspect>();

            Assert.IsTrue(entity.HasPersistedState());
        }

        [Test]
        public void HasPersistedState_AspectWithoutPersistedField_ReturnsFalse()
        {
            var entity = new Entity();
            entity.Require<RuntimeOnlyAspect>();

            Assert.IsFalse(entity.HasPersistedState());
        }
    }
}
