using NUnit.Framework;

namespace Rubickanov.GameplayTags.Tests
{
    [TestFixture]
    public class ReadOnlyGameplayTagContainerTests
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
        public void Default_IsEmpty()
        {
            var view = default(ReadOnlyGameplayTagContainer);

            Assert.IsTrue(view.IsEmpty);
            Assert.AreEqual(0, view.Count);
            Assert.IsFalse(view.HasTag(TagTestFixtures.Tag("Damage")));
        }

        [Test]
        public void Wrap_ExposesSourceCountAndQueries()
        {
            var source = TagTestFixtures.Container("Damage.Fire", "Status.Stun");
            var view = new ReadOnlyGameplayTagContainer(source);

            Assert.AreEqual(2, view.Count);
            Assert.IsTrue(view.HasTag(TagTestFixtures.Tag("Damage")));
            Assert.IsTrue(view.HasTagExact(TagTestFixtures.Tag("Damage.Fire")));
            Assert.IsFalse(view.HasTag(TagTestFixtures.Tag("Immune")));
        }

        [Test]
        public void Wrap_ReflectsSubsequentSourceMutations()
        {
            var source = new GameplayTagContainer();
            var view = new ReadOnlyGameplayTagContainer(source);
            Assert.AreEqual(0, view.Count);

            source.AddTag(TagTestFixtures.Tag("Damage"));

            Assert.AreEqual(1, view.Count);
            Assert.IsTrue(view.HasTagExact(TagTestFixtures.Tag("Damage")));
        }

        [Test]
        public void HasAll_AcceptsReadOnlyView()
        {
            var haystack = TagTestFixtures.Container("Damage.Fire", "Status.Stun");
            var needleSource = TagTestFixtures.Container("Damage");
            var needle = new ReadOnlyGameplayTagContainer(needleSource);

            Assert.IsTrue(haystack.HasAll(needle));
        }

        [Test]
        public void HasAny_AcceptsReadOnlyView()
        {
            var haystack = TagTestFixtures.Container("Damage.Fire");
            var needleSource = TagTestFixtures.Container("Status.Stun", "Damage");
            var needle = new ReadOnlyGameplayTagContainer(needleSource);

            Assert.IsTrue(haystack.HasAny(needle));
        }

        [Test]
        public void Enumerator_IteratesSourceTags()
        {
            var source = TagTestFixtures.Container("Damage", "Damage.Fire", "Status.Stun");
            var view = new ReadOnlyGameplayTagContainer(source);

            var count = 0;
            foreach (var _ in view)
                count++;

            Assert.AreEqual(3, count);
        }
    }
}
