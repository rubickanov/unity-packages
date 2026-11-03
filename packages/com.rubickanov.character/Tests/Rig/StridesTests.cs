using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    /// <summary>
    /// Stride markers and the stance frame: a two-stride loop with a cosine reach marks each foot once a stride where
    /// it reaches farthest ahead, also on the loop's last frame; the stance frame keeps to the bearing foot's half;
    /// feet that never pass each other have no strides.
    /// </summary>
    public sealed class StridesTests
    {
        private const int Stride = 60;
        private const int Spacing = 24;
        private const float Reach = 0.3f;

        private static float[] Cosine(int frames, int shift, float amplitude = 0.5f)
        {
            var reach = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                reach[f] = amplitude * Mathf.Cos(2f * Mathf.PI * (f - shift) / Stride);
            }

            return reach;
        }

        private static float[] Negated(float[] reach)
        {
            var negated = new float[reach.Length];
            for (int i = 0; i < reach.Length; i++)
            {
                negated[i] = -reach[i];
            }

            return negated;
        }

        [Test]
        public void MarksEachFootOnceAStride()
        {
            float[] reach = Cosine(2 * Stride, 0);
            Assert.That(Strides.FindMarkers(reach, true, Spacing, Reach), Is.EqualTo(new[] { 0, 60 }));
            Assert.That(Strides.FindMarkers(Negated(reach), true, Spacing, Reach), Is.EqualTo(new[] { 30, 90 }));
        }

        [Test]
        public void MarksAPeakOnTheLastFrame()
        {
            // The left foot is farthest ahead on the last frame: its neighbours wrap round to the first frames.
            float[] reach = Cosine(2 * Stride, -1);
            Assert.That(Strides.FindMarkers(reach, true, Spacing, Reach), Is.EqualTo(new[] { 59, 119 }));
        }

        [Test]
        public void StanceFrameKeepsToTheBearingFoot()
        {
            float[] reach = Cosine(2 * Stride, 0);
            int[] left = Strides.FindMarkers(reach, true, Spacing, Reach);
            int[] right = Strides.FindMarkers(Negated(reach), true, Spacing, Reach);
            // Feet level: mid-stance of the foot that bears, the other swinging past.
            Assert.That(Strides.StanceFrame(reach, left, right, 0f, true) % Stride, Is.EqualTo(15));
            Assert.That(Strides.StanceFrame(reach, left, right, 0f, false) % Stride, Is.EqualTo(45));
            // Left well ahead and bearing: it has just touched down.
            Assert.That(Strides.StanceFrame(reach, left, right, 0.45f, true) % Stride, Is.InRange(4, 5));
            // Left well ahead, the right bearing: the right is about to push off, the left about to touch down.
            Assert.That(Strides.StanceFrame(reach, left, right, 0.45f, false) % Stride, Is.InRange(55, 56));
        }

        [Test]
        public void FeetThatNeverPassHaveNoStrides()
        {
            float[] reach = Cosine(2 * Stride, 0, 0.2f);
            Assert.That(Strides.FindMarkers(reach, true, Spacing, Reach), Is.Empty);
            Assert.That(Strides.StanceFrame(reach, new int[0], new int[0], 0f, true), Is.EqualTo(-1));
        }
    }
}
