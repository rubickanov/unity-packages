using NUnit.Framework;

namespace Rubickanov.GameplayTags.Tests
{
    [TestFixture]
    public class GameplayTagTests
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
        public void None_IsNotValid()
        {
            Assert.IsFalse(GameplayTag.None.IsValid);
        }

        [Test]
        public void None_IndexIsZero()
        {
            Assert.AreEqual(0, GameplayTag.None.Index);
        }

        [Test]
        public void Equality_SameIndex_ReturnsTrue()
        {
            var a = TagTestFixtures.Tag("Damage.Fire");
            var b = TagTestFixtures.Tag("Damage.Fire");

            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void Equality_DifferentIndex_ReturnsFalse()
        {
            var fire = TagTestFixtures.Tag("Damage.Fire");
            var ice = TagTestFixtures.Tag("Damage.Ice");

            Assert.IsFalse(fire.Equals(ice));
            Assert.AreNotEqual(fire, ice);
        }

        [Test]
        public void OperatorEquals_MatchesEqualsMethod()
        {
            var a = TagTestFixtures.Tag("Damage.Fire");
            var b = TagTestFixtures.Tag("Damage.Fire");
            var c = TagTestFixtures.Tag("Damage.Ice");

            Assert.IsTrue(a == b);
            Assert.IsFalse(a == c);
            Assert.IsFalse(a != b);
            Assert.IsTrue(a != c);
        }

        [Test]
        public void GetHashCode_EqualsIndex()
        {
            var tag = TagTestFixtures.Tag("Damage.Fire");

            Assert.AreEqual(tag.Index, tag.GetHashCode());
        }

        [Test]
        public void MatchesExact_SameTag_ReturnsTrue()
        {
            var a = TagTestFixtures.Tag("Damage.Fire");
            var b = TagTestFixtures.Tag("Damage.Fire");

            Assert.IsTrue(a.MatchesExact(b));
        }

        [Test]
        public void MatchesExact_DifferentTag_ReturnsFalse()
        {
            var child = TagTestFixtures.Tag("Damage.Fire");
            var parent = TagTestFixtures.Tag("Damage");

            Assert.IsFalse(child.MatchesExact(parent));
        }

        [Test]
        public void Matches_ChildOfParent_ReturnsTrue()
        {
            var child = TagTestFixtures.Tag("Damage.Fire");
            var parent = TagTestFixtures.Tag("Damage");

            Assert.IsTrue(child.Matches(parent));
        }

        [Test]
        public void Matches_ParentOfChild_ReturnsFalse()
        {
            var parent = TagTestFixtures.Tag("Damage");
            var child = TagTestFixtures.Tag("Damage.Fire");

            Assert.IsFalse(parent.Matches(child));
        }

        [Test]
        public void Matches_Self_ReturnsTrue()
        {
            var tag = TagTestFixtures.Tag("Damage.Fire");

            Assert.IsTrue(tag.Matches(tag));
        }

        [Test]
        public void Matches_Unrelated_ReturnsFalse()
        {
            var fire = TagTestFixtures.Tag("Damage.Fire");
            var stun = TagTestFixtures.Tag("Status.Stun");

            Assert.IsFalse(fire.Matches(stun));
        }

        [Test]
        public void Matches_NoneParent_ReturnsFalse()
        {
            var tag = TagTestFixtures.Tag("Damage");

            Assert.IsFalse(tag.Matches(GameplayTag.None));
        }

        [Test]
        public void Matches_NoneChild_ReturnsFalse()
        {
            var tag = TagTestFixtures.Tag("Damage");

            Assert.IsFalse(GameplayTag.None.Matches(tag));
        }

        [Test]
        public void ToString_None_ReturnsNoneLiteral()
        {
            Assert.AreEqual("None", GameplayTag.None.ToString());
        }

        [Test]
        public void ToString_ResolvedTag_ReturnsPath()
        {
            var tag = TagTestFixtures.Tag("Damage.Fire");

            Assert.AreEqual("Damage.Fire", tag.ToString());
        }

        [Test]
        public void ToString_RegistryNotInstalled_ReturnsIndexFallback()
        {
            var tag = TagTestFixtures.Tag("Damage.Fire");
            var index = tag.Index;

            try
            {
                GameplayTagRegistry.Uninstall();

                Assert.AreEqual($"GameplayTag({index})", tag.ToString());
            }
            finally
            {
                TagTestFixtures.InstallStandardRegistry();
            }
        }
    }
}
