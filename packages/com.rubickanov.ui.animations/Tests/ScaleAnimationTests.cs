using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

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
                async () => await scale.PlayShowAsync(null!, CancellationToken.None).AsTask());
        }

        [Test]
        public void PlayHideAsync_NullTarget_Throws()
        {
            var scale = new ScaleAnimation();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await scale.PlayHideAsync(null!, CancellationToken.None).AsTask());
        }

        [Test]
        public void PlayShowAsync_SetsInitialScaleToStartScale()
        {
            var scale = new ScaleAnimation(0.3f);
            var target = new VisualElement();

            _ = scale.PlayShowAsync(target, CancellationToken.None);

            Assert.AreEqual(new Vector3(0.3f, 0.3f, 1f), target.style.scale.value.value);
        }

        [Test]
        public void DefaultCtor_UsesDefaultStartScale()
        {
            var scale = new ScaleAnimation();
            var target = new VisualElement();

            _ = scale.PlayShowAsync(target, CancellationToken.None);

            Assert.AreEqual(new Vector3(0.8f, 0.8f, 1f), target.style.scale.value.value);
        }

        [Test]
        public void Reset_ScaledTarget_ClearsInlineScale()
        {
            var scale = new ScaleAnimation();
            var target = new VisualElement();
            target.style.scale = new Scale(new Vector3(0.5f, 0.5f, 1f));

            scale.Reset(target);

            Assert.AreEqual(StyleKeyword.Null, target.style.scale.keyword);
        }
    }
}
