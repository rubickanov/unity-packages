using System.Collections.Generic;
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
    public class ObservableCollectionsSnapshotTests
    {
        private sealed class InventoryAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableList<int> Items = new();
        }

        private sealed class CooldownsAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableDictionary<string, float> Cooldowns = new();
        }

        private sealed class TagsAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableHashSet<int> Tags = new();
        }

        [Test]
        public void Snapshot_ObservableList_CapturesOrderedElements()
        {
            var entity = new Entity();
            var inv = entity.Require<InventoryAspect>();
            inv.Items.Add(10);
            inv.Items.Add(20);
            inv.Items.Add(30);

            var snap = entity.Snapshot();
            var payload = (List<int>)snap.Aspects[typeof(InventoryAspect).FullName].Fields["Items"];

            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, payload);
        }

        [Test]
        public void Snapshot_ObservableDictionary_CapturesAllEntries()
        {
            var entity = new Entity();
            var cd = entity.Require<CooldownsAspect>();
            cd.Cooldowns["fireball"] = 3f;
            cd.Cooldowns["heal"] = 1.5f;

            var snap = entity.Snapshot();
            var payload = (Dictionary<string, float>)snap.Aspects[typeof(CooldownsAspect).FullName].Fields["Cooldowns"];

            Assert.AreEqual(2, payload.Count);
            Assert.AreEqual(3f, payload["fireball"]);
            Assert.AreEqual(1.5f, payload["heal"]);
        }

        [Test]
        public void Snapshot_ObservableHashSet_CapturesAllElements()
        {
            var entity = new Entity();
            var tags = entity.Require<TagsAspect>();
            tags.Tags.Add(1);
            tags.Tags.Add(2);
            tags.Tags.Add(1); // duplicate is no-op for a set

            var snap = entity.Snapshot();
            var payload = (HashSet<int>)snap.Aspects[typeof(TagsAspect).FullName].Fields["Tags"];

            Assert.AreEqual(2, payload.Count);
            Assert.IsTrue(payload.Contains(1));
            Assert.IsTrue(payload.Contains(2));
        }

        [Test]
        public void Restore_ObservableList_ClearsExistingAndRefills()
        {
            var source = new Entity();
            source.Require<InventoryAspect>().Items.Add(1);
            source.Require<InventoryAspect>().Items.Add(2);
            var snap = source.Snapshot();

            var target = new Entity();
            var inv = target.Require<InventoryAspect>();
            inv.Items.Add(999);
            inv.Items.Add(888);

            target.Restore(snap);

            CollectionAssert.AreEqual(new[] { 1, 2 }, inv.Items);
        }

        [Test]
        public void Restore_ObservableDictionary_ClearsExistingAndRefills()
        {
            var source = new Entity();
            source.Require<CooldownsAspect>().Cooldowns["dash"] = 2f;
            var snap = source.Snapshot();

            var target = new Entity();
            var cd = target.Require<CooldownsAspect>();
            cd.Cooldowns["stale"] = 99f;

            target.Restore(snap);

            Assert.AreEqual(1, cd.Cooldowns.Count);
            Assert.AreEqual(2f, cd.Cooldowns["dash"]);
            Assert.IsFalse(cd.Cooldowns.ContainsKey("stale"));
        }

        [Test]
        public void Restore_ObservableHashSet_ClearsExistingAndRefills()
        {
            var source = new Entity();
            source.Require<TagsAspect>().Tags.Add(7);
            var snap = source.Snapshot();

            var target = new Entity();
            var tags = target.Require<TagsAspect>();
            tags.Tags.Add(42);

            target.Restore(snap);

            Assert.AreEqual(1, tags.Tags.Count);
            Assert.IsTrue(tags.Tags.Contains(7));
            Assert.IsFalse(tags.Tags.Contains(42));
        }

        [Test]
        public void Restore_ObservableList_TypeMismatch_PreservesLiveCollection()
        {
            // Regression: WriteValue used to Clear() before the (IEnumerable<int>) cast, so a
            // type-mismatched snapshot (here List<long> for an ObservableList<int>) left the live
            // collection emptied — the cast threw after the wipe. Casting before Clear keeps the
            // existing contents intact when the restore loop swallows the mismatch.
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("type mismatch"));

            var target = new Entity();
            var inv = target.Require<InventoryAspect>();
            inv.Items.Add(999);
            inv.Items.Add(888);

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Items"] = new List<long> { 1, 2 };
            snap.Aspects[typeof(InventoryAspect).FullName] = data;

            target.Restore(snap);

            CollectionAssert.AreEqual(new[] { 999, 888 }, inv.Items);
        }

        [Test]
        public void Restore_ObservableDictionary_TypeMismatch_PreservesLiveCollection()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("type mismatch"));

            var target = new Entity();
            var cd = target.Require<CooldownsAspect>();
            cd.Cooldowns["stale"] = 99f;

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Cooldowns"] = new Dictionary<string, int> { ["dash"] = 2 };
            snap.Aspects[typeof(CooldownsAspect).FullName] = data;

            target.Restore(snap);

            Assert.AreEqual(1, cd.Cooldowns.Count);
            Assert.AreEqual(99f, cd.Cooldowns["stale"]);
        }

        [Test]
        public void Restore_ObservableHashSet_TypeMismatch_PreservesLiveCollection()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("type mismatch"));

            var target = new Entity();
            var tags = target.Require<TagsAspect>();
            tags.Tags.Add(42);

            var snap = new AspectSnapshot();
            var data = new AspectData();
            data.Fields["Tags"] = new HashSet<long> { 7 };
            snap.Aspects[typeof(TagsAspect).FullName] = data;

            target.Restore(snap);

            Assert.AreEqual(1, tags.Tags.Count);
            Assert.IsTrue(tags.Tags.Contains(42));
        }

        [Test]
        public void Restore_ObservableList_FiresObserveAddForEachElement()
        {
            var source = new Entity();
            source.Require<InventoryAspect>().Items.Add(5);
            source.Require<InventoryAspect>().Items.Add(6);
            var snap = source.Snapshot();

            var target = new Entity();
            var inv = target.Require<InventoryAspect>();
            var added = new List<int>();
            using var sub = inv.Items.ObserveAdd().Subscribe(e => added.Add(e.Value));

            target.Restore(snap);

            CollectionAssert.AreEqual(new[] { 5, 6 }, added);
        }
    }
}
