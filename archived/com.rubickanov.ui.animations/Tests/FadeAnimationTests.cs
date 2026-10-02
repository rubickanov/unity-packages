using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Animations.Tests
{
    [TestFixture]
    public class FadeAnimationTests
    {
        [Test]
        public void PlayShowAsync_NullTarget_Throws()
        {
            var fade = new FadeAnimation();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await fade.PlayShowAsync(null!, CancellationToken.None).AsTask());
        }

        [Test]
        public void PlayShowAsync_SetsInitialOpacityToZero()
        {
            var fade = new FadeAnimation();
            var target = new VisualElement();

            _ = fade.PlayShowAsync(target, CancellationToken.None);

            Assert.AreEqual(0f, target.style.opacity.value);
        }

        [Test]
        public void Reset_FadedTarget_ClearsInlineOpacity()
        {
            var fade = new FadeAnimation();
            var target = new VisualElement();
            target.style.opacity = 0.5f;

            fade.Reset(target);

            Assert.AreEqual(StyleKeyword.Null, target.style.opacity.keyword);
        }
    }
}
