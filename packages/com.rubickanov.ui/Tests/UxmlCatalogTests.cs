using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class UxmlCatalogTests
    {
        private UxmlCatalog _catalog = null!;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<UxmlCatalog>();
            _catalog.name = "TestCatalog";
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_catalog);

        private static VisualTreeAsset Asset(string name)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = name;
            return asset;
        }

        [Test]
        public void TryGet_AssetInCatalog_FoundByName()
        {
            var asset = Asset(nameof(UxmlScreen));
            _catalog.SetAssets(new[] { Asset("Other"), asset });

            var found = _catalog.TryGet(nameof(UxmlScreen), out var result);

            Assert.IsTrue(found);
            Assert.AreSame(asset, result);
        }

        [Test]
        public void TryGet_DuplicateNames_ThrowsNamingTheAsset()
        {
            _catalog.SetAssets(new[] { Asset("Dup"), Asset("Dup") });

            var ex = Assert.Throws<InvalidOperationException>(() => _catalog.TryGet("Dup", out _));

            StringAssert.Contains("Dup", ex.Message);
            StringAssert.Contains("TestCatalog", ex.Message);
        }

        [Test]
        public void FromCatalog_AssetInCatalog_ReturnsItSynchronously()
        {
            var asset = Asset(nameof(UxmlScreen));
            _catalog.SetAssets(new[] { asset });
            var loader = UxmlLoaders.FromCatalog(_catalog);

            var task = loader(nameof(UxmlScreen));

            Assert.IsTrue(task.Status.IsCompleted());
            Assert.AreSame(asset, task.GetAwaiter().GetResult().asset);
        }

        [Test]
        public void FromCatalog_MissingName_ThrowsNamingCatalogAndView()
        {
            _catalog.SetAssets(Array.Empty<VisualTreeAsset>());
            var loader = UxmlLoaders.FromCatalog(_catalog);

            var ex = Assert.Throws<InvalidOperationException>(() => loader(nameof(UxmlScreen)));

            StringAssert.Contains("TestCatalog", ex.Message);
            StringAssert.Contains(nameof(UxmlScreen), ex.Message);
        }

        [Test]
        public void FindMissing_MixedViews_ReportsOnlyViewWithUxmlNameAndNoEntry()
        {
            _catalog.SetAssets(new[] { Asset(nameof(UxmlPopup)) });

            var missing = _catalog.FindMissing(new[] { typeof(UxmlScreen), typeof(UxmlPopup), typeof(ScreenA) });

            CollectionAssert.AreEqual(new[] { typeof(UxmlScreen) }, missing);
        }
    }
}
