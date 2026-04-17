using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Animations.Tests
{
    [TestFixture]
    public class ScaleAnimationTests
    {
        [Test]
        public void PlayShowAsync_NullTarget_Throws()
        {
            var scale = new ScaleAnimation();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await scale.PlayShowAsync(null!, 1f).AsTask());
        }

        [Test]
        public void PlayHideAsync_NullTarget_Throws()
        {
            var scale = new ScaleAnimation();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await scale.PlayHideAsync(null!, 1f).AsTask());
        }

        [Test]
        public void PlayShowAsync_SetsInitialScaleToStartScale()
        {
            var scale = new ScaleAnimation(0.3f);
            var target = new FakeAnimationTarget();

            _ = scale.PlayShowAsync(target, 1f);

            Assert.AreEqual(0.3f, target.ScaleX);
            Assert.AreEqual(0.3f, target.ScaleY);
        }

        [Test]
        public void DefaultCtor_UsesDefaultStartScale()
        {
            var scale = new ScaleAnimation();
            var target = new FakeAnimationTarget();

            _ = scale.PlayShowAsync(target, 1f);

            Assert.AreEqual(0.8f, target.ScaleX);
            Assert.AreEqual(0.8f, target.ScaleY);
        }
    }
}
