using System.Text.RegularExpressions;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class PersistedEnumTests
    {
        private enum TestMood
        {
            Neutral = 0,
            Happy = 1,
            Angry = 2,
        }

        private sealed class EnumWithoutAttributeAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<TestMood> Mood = new(TestMood.Neutral);
        }

        private sealed class EnumByNameAspect : IEntityAspect
        {
            [PersistedState] [PersistedEnum] public readonly ReactiveProperty<TestMood> Mood = new(TestMood.Neutral);
        }

        private sealed class EnumByValueAspect : IEntityAspect
        {
            [PersistedState] [PersistedEnum(PersistedEnumMode.ByValue)] public readonly ReactiveProperty<TestMood> Mood = new(TestMood.Neutral);
        }

        private sealed class EnumInListAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableList<TestMood> Moods = new();
        }

        [Test]
        public void Scan_ReactivePropertyOfEnumWithoutAttribute_LogsErrorAndSkips()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("\\[PersistedEnum\\]"));

            var entity = new Entity();
            entity.Require<EnumWithoutAttributeAspect>();

            var snap = entity.Snapshot();

            // Aspect has only one [PersistedState] field and it was rejected; no aspect entry written.
            Assert.IsFalse(snap.Aspects.ContainsKey(typeof(EnumWithoutAttributeAspect).FullName),
                "Aspect with only a rejected enum field produces no snapshot entry.");
        }

        [Test]
        public void Scan_EnumInCollection_LogsErrorAndSkips()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("enum inside a collection"));

            var entity = new Entity();
            entity.Require<EnumInListAspect>();

            var snap = entity.Snapshot();

            Assert.IsFalse(snap.Aspects.ContainsKey(typeof(EnumInListAspect).FullName));
        }

        [Test]
        public void Snapshot_EnumByName_WritesMemberName()
        {
            var entity = new Entity();
            entity.Require<EnumByNameAspect>().Mood.Value = TestMood.Happy;

            var snap = entity.Snapshot();

            var fields = snap.Aspects[typeof(EnumByNameAspect).FullName].Fields;
            Assert.AreEqual("Happy", fields["Mood"]);
        }

        [Test]
        public void Snapshot_EnumByValue_WritesUnderlyingLong()
        {
            var entity = new Entity();
            entity.Require<EnumByValueAspect>().Mood.Value = TestMood.Angry;

            var snap = entity.Snapshot();

            var fields = snap.Aspects[typeof(EnumByValueAspect).FullName].Fields;
            Assert.AreEqual(2L, fields["Mood"]);
        }

        [Test]
        public void Restore_EnumByName_ResolvesBackToEnum()
        {
            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Mood"] = "Angry";
            snap.Aspects[typeof(EnumByNameAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(TestMood.Angry, entity.Require<EnumByNameAspect>().Mood.Value);
        }

        [Test]
        public void Restore_EnumByValue_ResolvesBackToEnum()
        {
            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Mood"] = 1L;
            snap.Aspects[typeof(EnumByValueAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(TestMood.Happy, entity.Require<EnumByValueAspect>().Mood.Value);
        }

        [Test]
        public void Restore_EnumByName_UnknownMember_LogsWarningAndKeepsDefault()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("has no member"));

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Mood"] = "Morose"; // not a real enum member
            snap.Aspects[typeof(EnumByNameAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(TestMood.Neutral, entity.Require<EnumByNameAspect>().Mood.Value,
                "Unknown enum name must not corrupt the field — default survives.");
        }

        [Test]
        public void Restore_EnumByValue_UndefinedValue_LogsWarningAndKeepsDefault()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("is not defined"));

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Mood"] = 42L; // not a real enum value
            snap.Aspects[typeof(EnumByValueAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(TestMood.Neutral, entity.Require<EnumByValueAspect>().Mood.Value);
        }

        [Test]
        public void Restore_EnumDirectBoxedValue_IsAccepted()
        {
            // Serializer preserves the TEnum type end-to-end — binding should take it as-is
            // rather than forcing an unnecessary coercion.
            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Mood"] = TestMood.Happy;
            snap.Aspects[typeof(EnumByNameAspect).FullName] = data;

            var entity = new Entity();
            entity.Restore(snap);

            Assert.AreEqual(TestMood.Happy, entity.Require<EnumByNameAspect>().Mood.Value);
        }
    }
}
