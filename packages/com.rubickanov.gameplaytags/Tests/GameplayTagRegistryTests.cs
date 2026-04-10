using System;
using NUnit.Framework;

namespace Rubickanov.GameplayTags.Tests
{
    [TestFixture]
    public class GameplayTagRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            TagTestFixtures.EnsureUninstalled();
        }

        [TearDown]
        public void TearDown()
        {
            TagTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void Constructor_EmptyList_CountIsZero()
        {
            var registry = new GameplayTagRegistry(Array.Empty<string>());

            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(0, registry.GetAllTags().Count);
            Assert.AreEqual(0, registry.GetAllNames().Count);
        }

        [Test]
        public void Constructor_SingleRootTag_CountIsOne()
        {
            var registry = new GameplayTagRegistry(new[] { "Damage" });

            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet("Damage", out var tag));
            Assert.IsTrue(tag.IsValid);
        }

        [Test]
        public void Constructor_DeepPath_AutoCreatesAncestors()
        {
            var registry = new GameplayTagRegistry(new[] { "A.B.C" });

            Assert.AreEqual(3, registry.Count);
            Assert.IsTrue(registry.TryGet("A", out _));
            Assert.IsTrue(registry.TryGet("A.B", out _));
            Assert.IsTrue(registry.TryGet("A.B.C", out _));
        }

        [Test]
        public void Constructor_DuplicatePaths_AreDeduplicated()
        {
            var registry = new GameplayTagRegistry(new[] { "Damage", "Damage", "Damage" });

            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void Constructor_WhitespaceEntries_AreSkipped()
        {
            var registry = new GameplayTagRegistry(new[] { "Damage", "", "   ", null!, "Status" });

            Assert.AreEqual(2, registry.Count);
            Assert.IsTrue(registry.TryGet("Damage", out _));
            Assert.IsTrue(registry.TryGet("Status", out _));
        }

        [Test]
        public void Constructor_PathsWithSurroundingWhitespace_AreTrimmed()
        {
            var registry = new GameplayTagRegistry(new[] { "  Damage  " });

            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet("Damage", out var tag));
            Assert.IsTrue(tag.IsValid);
            Assert.IsFalse(registry.TryGet("  Damage  ", out _));
        }

        [Test]
        public void Constructor_TagsAreSortedLexicographically()
        {
            var registry = new GameplayTagRegistry(new[] { "Zeta", "Alpha", "Mike" });

            var names = registry.GetAllNames();
            Assert.AreEqual("Alpha", names[0]);
            Assert.AreEqual("Mike", names[1]);
            Assert.AreEqual("Zeta", names[2]);
        }

        [Test]
        public void Get_ExistingPath_ReturnsValidTag()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();

            var tag = registry.Get("Damage.Fire");

            Assert.IsTrue(tag.IsValid);
            Assert.AreEqual("Damage.Fire", registry.GetName(tag));
        }

        [Test]
        public void Get_UnknownPath_ThrowsArgumentException()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();

            Assert.Throws<ArgumentException>(() => registry.Get("Does.Not.Exist"));
        }

        [Test]
        public void TryGet_ExistingPath_ReturnsTrueAndTag()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();

            var found = registry.TryGet("Status.Stun", out var tag);

            Assert.IsTrue(found);
            Assert.IsTrue(tag.IsValid);
        }

        [Test]
        public void TryGet_UnknownPath_ReturnsFalseAndNone()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();

            var found = registry.TryGet("Unknown", out var tag);

            Assert.IsFalse(found);
            Assert.AreEqual(GameplayTag.None, tag);
        }

        [Test]
        public void GetName_ValidTag_ReturnsPath()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var tag = registry.Get("Damage.Fire.DoT");

            Assert.AreEqual("Damage.Fire.DoT", registry.GetName(tag));
        }

        [Test]
        public void GetName_NoneTag_ReturnsEmptyString()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();

            Assert.AreEqual("", registry.GetName(GameplayTag.None));
        }

        [Test]
        public void GetParent_RootTag_ReturnsNone()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var root = registry.Get("Damage");

            var parent = registry.GetParent(root);

            Assert.AreEqual(GameplayTag.None, parent);
        }

        [Test]
        public void GetParent_ChildTag_ReturnsParent()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var child = registry.Get("Damage.Fire");
            var expected = registry.Get("Damage");

            var parent = registry.GetParent(child);

            Assert.AreEqual(expected, parent);
        }

        [Test]
        public void GetParent_GrandchildTag_ReturnsImmediateParent()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var grandchild = registry.Get("Damage.Fire.DoT");
            var expected = registry.Get("Damage.Fire");

            var parent = registry.GetParent(grandchild);

            Assert.AreEqual(expected, parent);
        }

        [Test]
        public void GetDepth_RootTag_ReturnsOne()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var root = registry.Get("Damage");

            Assert.AreEqual(1, registry.GetDepth(root));
        }

        [Test]
        public void GetDepth_NestedTag_ReturnsSegmentCount()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var grandchild = registry.Get("Damage.Fire.DoT");

            Assert.AreEqual(3, registry.GetDepth(grandchild));
        }

        [Test]
        public void GetDepth_NoneTag_ReturnsZero()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();

            Assert.AreEqual(0, registry.GetDepth(GameplayTag.None));
        }

        [Test]
        public void Matches_ChildMatchesParent_ReturnsTrue()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var child = registry.Get("Damage.Fire");
            var parent = registry.Get("Damage");

            Assert.IsTrue(registry.Matches(child, parent));
        }

        [Test]
        public void Matches_ParentDoesNotMatchChild_ReturnsFalse()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var parent = registry.Get("Damage");
            var child = registry.Get("Damage.Fire");

            Assert.IsFalse(registry.Matches(parent, child));
        }

        [Test]
        public void Matches_Self_ReturnsTrue()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var tag = registry.Get("Damage.Fire");

            Assert.IsTrue(registry.Matches(tag, tag));
        }

        [Test]
        public void Matches_Sibling_ReturnsFalse()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var fire = registry.Get("Damage.Fire");
            var ice = registry.Get("Damage.Ice");

            Assert.IsFalse(registry.Matches(fire, ice));
        }

        [Test]
        public void Matches_DeepDescendantMatchesAncestor_ReturnsTrue()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var dot = registry.Get("Damage.Fire.DoT");
            var damage = registry.Get("Damage");

            Assert.IsTrue(registry.Matches(dot, damage));
        }

        [Test]
        public void Matches_NoneTag_ReturnsFalse()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();
            var tag = registry.Get("Damage");

            Assert.IsFalse(registry.Matches(GameplayTag.None, tag));
            Assert.IsFalse(registry.Matches(tag, GameplayTag.None));
        }

        [Test]
        public void GetAllTags_ReturnsAllRegisteredTagsInOrder()
        {
            var registry = new GameplayTagRegistry(new[] { "Zeta", "Alpha", "Mike" });

            var tags = registry.GetAllTags();

            Assert.AreEqual(3, tags.Count);
            Assert.AreEqual("Alpha", registry.GetName(tags[0]));
            Assert.AreEqual("Mike", registry.GetName(tags[1]));
            Assert.AreEqual("Zeta", registry.GetName(tags[2]));
        }

        [Test]
        public void GetAllNames_CountMatchesGetAllTags()
        {
            var registry = TagTestFixtures.BuildStandardRegistry();

            Assert.AreEqual(registry.GetAllTags().Count, registry.GetAllNames().Count);
        }

        [Test]
        public void Install_WhenNotInstalled_SetsIsInstalledTrue()
        {
            Assert.IsFalse(GameplayTagRegistry.IsInstalled);

            GameplayTagRegistry.Install(TagTestFixtures.BuildStandardRegistry());

            Assert.IsTrue(GameplayTagRegistry.IsInstalled);
        }

        [Test]
        public void Install_WhenAlreadyInstalled_Throws()
        {
            GameplayTagRegistry.Install(TagTestFixtures.BuildStandardRegistry());

            Assert.Throws<InvalidOperationException>(
                () => GameplayTagRegistry.Install(TagTestFixtures.BuildStandardRegistry()));
        }

        [Test]
        public void Uninstall_ClearsIsInstalled()
        {
            GameplayTagRegistry.Install(TagTestFixtures.BuildStandardRegistry());

            GameplayTagRegistry.Uninstall();

            Assert.IsFalse(GameplayTagRegistry.IsInstalled);
        }

        [Test]
        public void Uninstall_WhenNotInstalled_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => GameplayTagRegistry.Uninstall());
        }

        [Test]
        public void Instance_WhenNotInstalled_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => { _ = GameplayTagRegistry.Instance; });
        }
    }
}
