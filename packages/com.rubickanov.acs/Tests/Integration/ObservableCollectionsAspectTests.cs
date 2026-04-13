using System.Collections.Generic;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Reactive collections (Cysharp ObservableCollections + R3 bridge) as aspect fields.
    /// The point of these tests is to lock the asmdef wiring — if ObservableCollections.dll
    /// or ObservableCollections.R3.dll falls out of ACS.Runtime/ACS.Tests precompiled
    /// references, these fail at compile time. We do not re-prove Cysharp's own contract.
    /// </summary>
    [TestFixture]
    public class ObservableCollectionsAspectTests
    {
        private sealed class InventoryAspect : IEntityAspect
        {
            public readonly ObservableList<int> Items = new();
        }

        private sealed class CooldownsAspect : IEntityAspect
        {
            public readonly ObservableDictionary<string, float> Cooldowns = new();
        }

        private sealed class TagsAspect : IEntityAspect
        {
            public readonly ObservableHashSet<string> Tags = new();
        }

        private sealed class DamageLogAspect : IEntityAspect
        {
            public readonly ObservableFixedSizeRingBuffer<int> Log = new(capacity: 3);
        }

        [Test]
        public void ObservableList_AddItem_ObserveAddFiresWithIndexAndValue()
        {
            var entity = new Entity();
            var inventory = entity.Require<InventoryAspect>();
            var events = new List<CollectionAddEvent<int>>();
            using var sub = inventory.Items.ObserveAdd().Subscribe(e => events.Add(e));

            inventory.Items.Add(42);
            inventory.Items.Add(7);

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(0, events[0].Index);
            Assert.AreEqual(42, events[0].Value);
            Assert.AreEqual(1, events[1].Index);
            Assert.AreEqual(7, events[1].Value);

            entity.Dispose();
        }

        [Test]
        public void ObservableDictionary_AddThenUpdate_ObserveAddAndReplaceFire()
        {
            var entity = new Entity();
            var cd = entity.Require<CooldownsAspect>();
            var added = new List<KeyValuePair<string, float>>();
            var replaced = new List<KeyValuePair<string, float>>();
            using var subAdd = cd.Cooldowns.ObserveAdd().Subscribe(e => added.Add(e.Value));
            using var subReplace = cd.Cooldowns.ObserveReplace().Subscribe(e => replaced.Add(e.NewValue));

            cd.Cooldowns["fireball"] = 5f;
            cd.Cooldowns["fireball"] = 2.5f;

            Assert.AreEqual(1, added.Count);
            Assert.AreEqual("fireball", added[0].Key);
            Assert.AreEqual(5f, added[0].Value);
            Assert.AreEqual(1, replaced.Count);
            Assert.AreEqual(2.5f, replaced[0].Value);

            entity.Dispose();
        }

        [Test]
        public void ObservableHashSet_AddDuplicate_DoesNotDoubleFire()
        {
            var entity = new Entity();
            var tags = entity.Require<TagsAspect>();
            int addCount = 0;
            using var sub = tags.Tags.ObserveAdd().Subscribe(_ => addCount++);

            tags.Tags.Add("stunned");
            tags.Tags.Add("stunned");
            tags.Tags.Add("burning");

            Assert.AreEqual(2, addCount, "HashSet must not raise add for a duplicate key.");
            Assert.AreEqual(2, tags.Tags.Count);

            entity.Dispose();
        }

        [Test]
        public void ObservableFixedSizeRingBuffer_OverflowCapacity_ObserveRemoveFiresForOldest()
        {
            var entity = new Entity();
            var dmg = entity.Require<DamageLogAspect>();
            var removed = new List<int>();
            using var sub = dmg.Log.ObserveRemove().Subscribe(e => removed.Add(e.Value));

            dmg.Log.AddLast(10);
            dmg.Log.AddLast(20);
            dmg.Log.AddLast(30);
            dmg.Log.AddLast(40);

            Assert.AreEqual(1, removed.Count,
                "Pushing a 4th item into a capacity-3 ring must evict the oldest (10).");
            Assert.AreEqual(10, removed[0]);
            Assert.AreEqual(3, dmg.Log.Count);

            entity.Dispose();
        }
    }
}
