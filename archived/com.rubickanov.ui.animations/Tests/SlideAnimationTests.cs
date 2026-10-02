using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Animations.Tests
{
    [TestFixture]
    public class SlideAnimationTests
    {
        [Test]
        public void Ctor_NegativeOffset_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SlideAnimation(SlideDirection.Left, -1f));
        }

        [Test]
        public void Ctor_ZeroOffset_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new SlideAnimation(SlideDirection.Left, 0f));
        }

        [Test]
        public void PlayShowAsync_NullTarget_Throws()
        {
            var slide = new SlideAnimation(SlideDirection.Left);

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await slide.PlayShowAsync(null!, CancellationToken.None).AsTask());
        }

        [Test]
        public void PlayHideAsync_NullTarget_Throws()
        {
            var slide = new SlideAnimation(SlideDirection.Left);

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await slide.PlayHideAsync(null!, CancellationToken.None).AsTask());
        }

        [Test]
        public void PlayShowAsync_FromLeft_SetsInitialTranslateToNegativeOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Left, 50f);
            var target = new VisualElement();

            _ = slide.PlayShowAsync(target, CancellationToken.None);

            Assert.AreEqual(-50f, target.style.translate.value.x.value);
            Assert.AreEqual(0f, target.style.translate.value.y.value);
        }

        [Test]
        public void PlayShowAsync_FromRight_SetsInitialTranslateToPositiveOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Right, 50f);
            var target = new VisualElement();

            _ = slide.PlayShowAsync(target, CancellationToken.None);

            Assert.AreEqual(50f, target.style.translate.value.x.value);
            Assert.AreEqual(0f, target.style.translate.value.y.value);
        }

        [Test]
        public void PlayShowAsync_FromTop_SetsInitialTranslateToNegativeYOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Top, 50f);
            var target = new VisualElement();

            _ = slide.PlayShowAsync(target, CancellationToken.None);

            Assert.AreEqual(0f, target.style.translate.value.x.value);
            Assert.AreEqual(-50f, target.style.translate.value.y.value);
        }

        [Test]
        public void PlayShowAsync_FromBottom_SetsInitialTranslateToPositiveYOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Bottom, 50f);
            var target = new VisualElement();

            _ = slide.PlayShowAsync(target, CancellationToken.None);

            Assert.AreEqual(0f, target.style.translate.value.x.value);
            Assert.AreEqual(50f, target.style.translate.value.y.value);
        }

        [Test]
        public void Reset_TranslatedTarget_ClearsInlineTranslate()
        {
            var slide = new SlideAnimation(SlideDirection.Left);
            var target = new VisualElement();
            target.style.translate = new Translate(-50f, 0f);

            slide.Reset(target);

            Assert.AreEqual(StyleKeyword.Null, target.style.translate.keyword);
        }
    }
}
