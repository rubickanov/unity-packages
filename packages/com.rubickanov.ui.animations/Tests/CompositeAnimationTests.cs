using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Animations.Tests
{
    [TestFixture]
    public class CompositeAnimationTests
    {
        [Test]
        public void Ctor_NullAnimationsArray_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CompositeAnimation(null!));
        }

        [Test]
        public void Ctor_EmptyAnimationsArray_Throws()
        {
            Assert.Throws<ArgumentException>(() => new CompositeAnimation());
        }

        [Test]
        public void PlayShowAsync_NullTarget_Throws()
        {
            var composite = new CompositeAnimation(FadeAnimation.Instance);

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await composite.PlayShowAsync(null!, 1f).AsTask());
        }

        [Test]
        public void PlayHideAsync_NullTarget_Throws()
        {
            var composite = new CompositeAnimation(FadeAnimation.Instance);

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await composite.PlayHideAsync(null!, 1f).AsTask());
        }
    }
}
