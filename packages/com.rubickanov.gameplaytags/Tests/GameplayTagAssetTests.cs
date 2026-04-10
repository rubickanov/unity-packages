using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.GameplayTags.Tests
{
    [TestFixture]
    public class GameplayTagAssetTests
    {
        private GameplayTagAsset _asset;

        [SetUp]
        public void SetUp()
        {
            TagTestFixtures.EnsureUninstalled();
            _asset = ScriptableObject.CreateInstance<GameplayTagAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
                Object.DestroyImmediate(_asset);
            TagTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void TagPaths_NewAsset_IsEmpty()
        {
            Assert.AreEqual(0, _asset.TagPaths.Count);
        }

        [Test]
        public void SetTagPaths_UpdatesTagPaths()
        {
            _asset.SetTagPaths(new[] { "Damage", "Status.Stun" });

            Assert.AreEqual(2, _asset.TagPaths.Count);
            Assert.AreEqual("Damage", _asset.TagPaths[0]);
            Assert.AreEqual("Status.Stun", _asset.TagPaths[1]);
        }

        [Test]
        public void BuildRegistry_EmptyPaths_ReturnsEmptyRegistry()
        {
            var registry = _asset.BuildRegistry();

            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void BuildRegistry_ValidPaths_ReturnsPopulatedRegistry()
        {
            _asset.SetTagPaths(new[] { "Damage", "Status.Stun" });

            var registry = _asset.BuildRegistry();

            Assert.AreEqual(3, registry.Count); // Damage, Status, Status.Stun
            Assert.IsTrue(registry.TryGet("Damage", out _));
            Assert.IsTrue(registry.TryGet("Status", out _));
            Assert.IsTrue(registry.TryGet("Status.Stun", out _));
        }

        [Test]
        public void BuildRegistry_AutoCreatesParents()
        {
            _asset.SetTagPaths(new[] { "A.B.C.D" });

            var registry = _asset.BuildRegistry();

            Assert.AreEqual(4, registry.Count);
            Assert.IsTrue(registry.TryGet("A", out _));
            Assert.IsTrue(registry.TryGet("A.B", out _));
            Assert.IsTrue(registry.TryGet("A.B.C", out _));
            Assert.IsTrue(registry.TryGet("A.B.C.D", out _));
        }

        [Test]
        public void BuildRegistry_DoesNotAutoInstall()
        {
            _asset.SetTagPaths(new[] { "Damage" });

            _asset.BuildRegistry();

            Assert.IsFalse(GameplayTagRegistry.IsInstalled);
        }
    }
}
