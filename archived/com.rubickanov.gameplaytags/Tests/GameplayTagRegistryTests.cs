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

        [TestCase("A..B")]
        [TestCase(".A")]
        [TestCase("A.")]
        [TestCase("A B")]
        [TestCase("A.B C")]
        [TestCase("1A")]
        [TestCase("A-B")]
        [TestCase("A.1B")]
        [TestCase(".")]
        public void Constructor_InvalidPath_Throws(string invalidPath)
        {
            Assert.Throws<ArgumentException>(() => new GameplayTagRegistry(new[] { invalidPath }));
        }

        [Test]
        public void Constructor_ValidAlphanumericPath_Accepted()
        {
            var registry = new GameplayTagRegistry(new[] { "A1.B2C3.D" });

            Assert.AreEqual(3, registry.Count);
            Assert.IsTrue(registry.TryGet("A1", out _));
            Assert.IsTrue(registry.TryGet("A1.B2C3", out _));
            Assert.IsTrue(registry.TryGet("A1.B2C3.D", out _));
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
        public void AddTags_NewPaths_GetAppendedWithNewIndices()
        {
            var registry = new GameplayTagRegistry(new[] { "Damage" });
            var existing = registry.Get("Damage");

            registry.AddTags(new[] { "Status", "Buff" });

            Assert.AreEqual(3, registry.Count);
            Assert.AreEqual(existing, registry.Get("Damage"));
            Assert.IsTrue(registry.TryGet("Status", out var status));
            Assert.IsTrue(registry.TryGet("Buff", out var buff));
            Assert.IsTrue(status.IsValid);
            Assert.IsTrue(buff.IsValid);
            Assert.AreNotEqual(existing, status);
            Assert.AreNotEqual(existing, buff);
            Assert.AreNotEqual(status, buff);
        }

        [Test]
        public void AddTags_ExistingPath_IsNoOp()
        {
            var registry = new GameplayTagRegistry(new[] { "Damage", "Damage.Fire" });
            var damage = registry.Get("Damage");
            var fire = registry.Get("Damage.Fire");

            registry.AddTags(new[] { "Damage", "Damage.Fire" });

            Assert.AreEqual(2, registry.Count);
            Assert.AreEqual(damage, registry.Get("Damage"));
            Assert.AreEqual(fire, registry.Get("Damage.Fire"));
        }

        [Test]
        public void AddTags_CreatesMissingParents()
        {
            var registry = new GameplayTagRegistry(Array.Empty<string>());

            registry.AddTags(new[] { "A.B.C" });

            Assert.AreEqual(3, registry.Count);
            Assert.IsTrue(registry.TryGet("A", out _));
            Assert.IsTrue(registry.TryGet("A.B", out _));
            Assert.IsTrue(registry.TryGet("A.B.C", out _));

            var leaf = registry.Get("A.B.C");
            var mid = registry.Get("A.B");
            var root = registry.Get("A");
            Assert.AreEqual(mid, registry.GetParent(leaf));
            Assert.AreEqual(root, registry.GetParent(mid));
            Assert.AreEqual(GameplayTag.None, registry.GetParent(root));
        }

        [Test]
        public void AddTags_PreservesExistingTagIndices()
        {
            var registry = new GameplayTagRegistry(new[] { "Damage", "Damage.Fire" });
            var damageBefore = registry.Get("Damage");
            var fireBefore = registry.Get("Damage.Fire");

            registry.AddTags(new[] { "Aaa", "Aaa.Bbb", "Status.Stun" });

            Assert.AreEqual(damageBefore, registry.Get("Damage"));
            Assert.AreEqual(fireBefore, registry.Get("Damage.Fire"));
        }

        [TestCase("A..B")]
        [TestCase(".A")]
        [TestCase("A.")]
        [TestCase("1A")]
        [TestCase("A-B")]
        public void AddTags_InvalidPath_Throws(string invalidPath)
        {
            var registry = new GameplayTagRegistry(new[] { "Damage" });

            Assert.Throws<ArgumentException>(() => registry.AddTags(new[] { invalidPath }));
        }

        [Test]
        public void AddTags_NullArgument_Throws()
        {
            var registry = new GameplayTagRegistry(Array.Empty<string>());

            Assert.Throws<ArgumentNullException>(() => registry.AddTags(null!));
        }

        [Test]
        public void AddTags_AfterInstall_VisibleThroughInstance()
        {
            var registry = new GameplayTagRegistry(new[] { "Damage" });
            GameplayTagRegistry.Install(registry);

            GameplayTagRegistry.Instance.AddTags(new[] { "Status.Stun" });

            Assert.IsTrue(GameplayTagRegistry.Instance.TryGet("Status.Stun", out var stun));
            Assert.IsTrue(stun.IsValid);
            Assert.AreEqual(3, GameplayTagRegistry.Instance.Count);
        }

        [Test]
        public void AddTags_UpdatesSortedViews()
        {
            var registry = new GameplayTagRegistry(new[] { "Mike" });
            var namesBefore = registry.GetAllNames();
            Assert.AreEqual(1, namesBefore.Count);

            registry.AddTags(new[] { "Alpha", "Zeta" });

            var namesAfter = registry.GetAllNames();
            Assert.AreEqual(3, namesAfter.Count);
            Assert.AreEqual("Alpha", namesAfter[0]);
            Assert.AreEqual("Mike", namesAfter[1]);
            Assert.AreEqual("Zeta", namesAfter[2]);
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
        public void Matches_StaleOutOfRangeTag_ReturnsFalseWithoutThrowing()
        {
            var big = new GameplayTagRegistry(new[] { "A", "B", "C", "D", "E", "F", "G", "H" });
            var staleTag = big.Get("H");
            var small = new GameplayTagRegistry(new[] { "X" });
            var localTag = small.Get("X");

            Assert.DoesNotThrow(() => small.Matches(staleTag, localTag));
            Assert.IsFalse(small.Matches(staleTag, localTag));
            Assert.IsFalse(small.Matches(localTag, staleTag));
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
