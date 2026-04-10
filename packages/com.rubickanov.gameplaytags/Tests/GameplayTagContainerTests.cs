using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.GameplayTags.Tests
{
    [TestFixture]
    public class GameplayTagContainerTests
    {
        [SetUp]
        public void SetUp()
        {
            TagTestFixtures.InstallStandardRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            TagTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void NewContainer_IsEmpty()
        {
            var container = new GameplayTagContainer();

            Assert.IsTrue(container.IsEmpty);
            Assert.AreEqual(0, container.Count);
        }

        [Test]
        public void AddTag_ValidTag_IncreasesCount()
        {
            var container = new GameplayTagContainer();

            container.AddTag(TagTestFixtures.Tag("Damage.Fire"));

            Assert.AreEqual(1, container.Count);
            Assert.IsFalse(container.IsEmpty);
        }

        [Test]
        public void AddTag_Duplicate_IsNoOp()
        {
            var container = new GameplayTagContainer();
            var tag = TagTestFixtures.Tag("Damage.Fire");

            container.AddTag(tag);
            container.AddTag(tag);
            container.AddTag(tag);

            Assert.AreEqual(1, container.Count);
        }

        [Test]
        public void AddTag_None_IsNoOp()
        {
            var container = new GameplayTagContainer();

            container.AddTag(GameplayTag.None);

            Assert.AreEqual(0, container.Count);
            Assert.IsTrue(container.IsEmpty);
        }

        [Test]
        public void AddTag_MaintainsSortedOrder()
        {
            var container = new GameplayTagContainer();

            // Insert out of order
            container.AddTag(TagTestFixtures.Tag("Status.Stun"));
            container.AddTag(TagTestFixtures.Tag("Damage"));
            container.AddTag(TagTestFixtures.Tag("Immune"));

            var indices = new List<int>();
            foreach (var tag in container)
                indices.Add(tag.Index);

            for (var i = 1; i < indices.Count; i++)
                Assert.Less(indices[i - 1], indices[i]);
        }

        [Test]
        public void RemoveTag_Existing_ReturnsTrueAndRemoves()
        {
            var container = new GameplayTagContainer();
            var tag = TagTestFixtures.Tag("Damage.Fire");
            container.AddTag(tag);

            var removed = container.RemoveTag(tag);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, container.Count);
        }

        [Test]
        public void RemoveTag_Missing_ReturnsFalse()
        {
            var container = new GameplayTagContainer();

            var removed = container.RemoveTag(TagTestFixtures.Tag("Damage.Fire"));

            Assert.IsFalse(removed);
        }

        [Test]
        public void Clear_RemovesAllTags()
        {
            var container = TagTestFixtures.Container("Damage.Fire", "Status.Stun", "Immune");

            container.Clear();

            Assert.IsTrue(container.IsEmpty);
            Assert.AreEqual(0, container.Count);
        }

        [Test]
        public void From_Params_AddsAllTags()
        {
            var container = GameplayTagContainer.From(
                TagTestFixtures.Tag("Damage.Fire"),
                TagTestFixtures.Tag("Status.Stun"));

            Assert.AreEqual(2, container.Count);
        }

        [Test]
        public void From_Duplicates_Dedups()
        {
            var tag = TagTestFixtures.Tag("Damage.Fire");
            var container = GameplayTagContainer.From(tag, tag, tag);

            Assert.AreEqual(1, container.Count);
        }

        [Test]
        public void HasTag_Self_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire");

            Assert.IsTrue(container.HasTag(TagTestFixtures.Tag("Damage.Fire")));
        }

        [Test]
        public void HasTag_ParentWhenChildPresent_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire.DoT");

            Assert.IsTrue(container.HasTag(TagTestFixtures.Tag("Damage")));
            Assert.IsTrue(container.HasTag(TagTestFixtures.Tag("Damage.Fire")));
        }

        [Test]
        public void HasTag_ChildWhenParentPresent_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage");

            Assert.IsFalse(container.HasTag(TagTestFixtures.Tag("Damage.Fire")));
        }

        [Test]
        public void HasTag_Unrelated_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");

            Assert.IsFalse(container.HasTag(TagTestFixtures.Tag("Status.Stun")));
        }

        [Test]
        public void HasTag_NoneQuery_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");

            Assert.IsFalse(container.HasTag(GameplayTag.None));
        }

        [Test]
        public void HasTagExact_Present_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire");

            Assert.IsTrue(container.HasTagExact(TagTestFixtures.Tag("Damage.Fire")));
        }

        [Test]
        public void HasTagExact_AncestorPresent_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");

            Assert.IsFalse(container.HasTagExact(TagTestFixtures.Tag("Damage")));
        }

        [Test]
        public void HasAll_EmptyQuery_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var empty = new GameplayTagContainer();

            Assert.IsTrue(container.HasAll(empty));
        }

        [Test]
        public void HasAll_AllMatch_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire", "Status.Stun");
            var query = TagTestFixtures.Container("Damage.Fire", "Status.Stun");

            Assert.IsTrue(container.HasAll(query));
        }

        [Test]
        public void HasAll_OneMissing_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var query = TagTestFixtures.Container("Damage.Fire", "Status.Stun");

            Assert.IsFalse(container.HasAll(query));
        }

        [Test]
        public void HasAll_HierarchyMatch_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire.DoT", "Status.Stun");
            var query = TagTestFixtures.Container("Damage", "Status");

            Assert.IsTrue(container.HasAll(query));
        }

        [Test]
        public void HasAny_EmptyQuery_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var empty = new GameplayTagContainer();

            Assert.IsFalse(container.HasAny(empty));
        }

        [Test]
        public void HasAny_NoneMatch_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var query = TagTestFixtures.Container("Status.Stun", "Immune");

            Assert.IsFalse(container.HasAny(query));
        }

        [Test]
        public void HasAny_SingleMatch_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire.DoT");
            var query = TagTestFixtures.Container("Damage", "Status.Stun");

            Assert.IsTrue(container.HasAny(query));
        }

        [Test]
        public void HasAllExact_EmptyQuery_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var empty = new GameplayTagContainer();

            Assert.IsTrue(container.HasAllExact(empty));
        }

        [Test]
        public void HasAllExact_AncestorOnly_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var query = TagTestFixtures.Container("Damage");

            Assert.IsFalse(container.HasAllExact(query));
        }

        [Test]
        public void HasAnyExact_EmptyQuery_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var empty = new GameplayTagContainer();

            Assert.IsFalse(container.HasAnyExact(empty));
        }

        [Test]
        public void HasAnyExact_ExactMatch_ReturnsTrue()
        {
            var container = TagTestFixtures.Container("Damage.Fire", "Status.Stun");
            var query = TagTestFixtures.Container("Damage.Fire");

            Assert.IsTrue(container.HasAnyExact(query));
        }

        [Test]
        public void HasAnyExact_AncestorOnly_ReturnsFalse()
        {
            var container = TagTestFixtures.Container("Damage.Fire");
            var query = TagTestFixtures.Container("Damage");

            Assert.IsFalse(container.HasAnyExact(query));
        }

        [Test]
        public void Enumerator_EnumeratesAllTagsInOrder()
        {
            var container = new GameplayTagContainer();
            var fire = TagTestFixtures.Tag("Damage.Fire");
            var stun = TagTestFixtures.Tag("Status.Stun");
            container.AddTag(stun);
            container.AddTag(fire);

            var collected = new List<GameplayTag>();
            foreach (var tag in container)
                collected.Add(tag);

            Assert.AreEqual(2, collected.Count);
            // Sorted by index
            Assert.Less(collected[0].Index, collected[1].Index);
        }

        [Test]
        public void Enumerator_Empty_CompletesImmediately()
        {
            var container = new GameplayTagContainer();

            var count = 0;
            foreach (var _ in container)
                count++;

            Assert.AreEqual(0, count);
        }

        [Test]
        public void Enumerator_Reset_RestartsIteration()
        {
            var container = TagTestFixtures.Container("Damage", "Damage.Fire", "Status.Stun");

            var enumerator = container.GetEnumerator();
            var first = new List<int>();
            while (enumerator.MoveNext())
                first.Add(enumerator.Current.Index);

            enumerator.Reset();
            var second = new List<int>();
            while (enumerator.MoveNext())
                second.Add(enumerator.Current.Index);

            CollectionAssert.AreEqual(first, second);
        }
    }
}
