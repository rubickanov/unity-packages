using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

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
                async () => await slide.PlayShowAsync(null!, 1f).AsTask());
        }

        [Test]
        public void PlayHideAsync_NullTarget_Throws()
        {
            var slide = new SlideAnimation(SlideDirection.Left);

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await slide.PlayHideAsync(null!, 1f).AsTask());
        }

        [Test]
        public void PlayShowAsync_FromLeft_SetsInitialTranslateToNegativeOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Left, 50f);
            var target = new FakeAnimationTarget();

            _ = slide.PlayShowAsync(target, 1f);

            Assert.AreEqual(-50f, target.TranslateX);
            Assert.AreEqual(0f, target.TranslateY);
        }

        [Test]
        public void PlayShowAsync_FromRight_SetsInitialTranslateToPositiveOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Right, 50f);
            var target = new FakeAnimationTarget();

            _ = slide.PlayShowAsync(target, 1f);

            Assert.AreEqual(50f, target.TranslateX);
            Assert.AreEqual(0f, target.TranslateY);
        }

        [Test]
        public void PlayShowAsync_FromTop_SetsInitialTranslateToNegativeYOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Top, 50f);
            var target = new FakeAnimationTarget();

            _ = slide.PlayShowAsync(target, 1f);

            Assert.AreEqual(0f, target.TranslateX);
            Assert.AreEqual(-50f, target.TranslateY);
        }

        [Test]
        public void PlayShowAsync_FromBottom_SetsInitialTranslateToPositiveYOffset()
        {
            var slide = new SlideAnimation(SlideDirection.Bottom, 50f);
            var target = new FakeAnimationTarget();

            _ = slide.PlayShowAsync(target, 1f);

            Assert.AreEqual(0f, target.TranslateX);
            Assert.AreEqual(50f, target.TranslateY);
        }
    }
}
