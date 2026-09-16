using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

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
            var composite = new CompositeAnimation(new FadeAnimation());

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await composite.PlayShowAsync(null!, CancellationToken.None).AsTask());
        }

        [Test]
        public void PlayHideAsync_NullTarget_Throws()
        {
            var composite = new CompositeAnimation(new FadeAnimation());

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await composite.PlayHideAsync(null!, CancellationToken.None).AsTask());
        }

        [Test]
        public void Reset_ResetsEveryChildAnimation()
        {
            var composite = new CompositeAnimation(new FadeAnimation(), new ScaleAnimation());
            var target = new VisualElement();
            target.style.opacity = 0.5f;
            target.style.scale = new Scale(new Vector3(0.5f, 0.5f, 1f));

            composite.Reset(target);

            Assert.AreEqual(StyleKeyword.Null, target.style.opacity.keyword);
            Assert.AreEqual(StyleKeyword.Null, target.style.scale.keyword);
        }
    }
}
