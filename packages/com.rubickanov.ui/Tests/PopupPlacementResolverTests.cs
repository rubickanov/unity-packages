using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class PopupPlacementResolverTests
    {
        private static readonly Vector2 LayerSize = new(800f, 600f);
        private static readonly Vector2 PanelSize = new(100f, 50f);

        [Test]
        public void TryResolve_WorldOffScreenNoClamp_StaysOffScreen()
        {
            var anchor = new GameObject("anchor");
            try
            {
                var placement = PopupPlacement.AtWorld(anchor.transform, clampToScreen: false);

                var visible = PopupPlacementResolver.TryResolve(placement, PanelSize, LayerSize, Vector2.zero,
                    new Vector2(2000f, -300f), out var topLeft, out _);

                Assert.IsTrue(visible);
                Assert.AreEqual(new Vector2(1950f, -350f), topLeft);
            }
            finally
            {
                Object.DestroyImmediate(anchor);
            }
        }

        [Test]
        public void TryResolve_WorldOffScreenDefault_Clamped()
        {
            var anchor = new GameObject("anchor");
            try
            {
                var placement = PopupPlacement.AtWorld(anchor.transform);

                PopupPlacementResolver.TryResolve(placement, PanelSize, LayerSize, Vector2.zero,
                    new Vector2(2000f, -300f), out var topLeft, out _);

                Assert.AreEqual(new Vector2(700f, 0f), topLeft);
            }
            finally
            {
                Object.DestroyImmediate(anchor);
            }
        }

        [Test]
        public void TryResolve_WorldScreenOffset_AddedAfterProjection()
        {
            var anchor = new GameObject("anchor");
            try
            {
                var placement = PopupPlacement.AtWorld(anchor.transform, screenOffset: new Vector2(10f, 20f));

                PopupPlacementResolver.TryResolve(placement, PanelSize, LayerSize, Vector2.zero,
                    new Vector2(400f, 300f), out var topLeft, out _);

                Assert.AreEqual(new Vector2(360f, 270f), topLeft);
            }
            finally
            {
                Object.DestroyImmediate(anchor);
            }
        }

        [Test]
        public void ScreenToPanel_ScaledPanel_FlipsYAndScales()
        {
            var panel = PopupPlacementResolver.ScreenToPanel(new Vector2(400f, 100f), new Vector2(1600f, 1200f),
                new Vector2(800f, 600f));

            Assert.AreEqual(new Vector2(200f, 550f), panel);
        }
    }
}
