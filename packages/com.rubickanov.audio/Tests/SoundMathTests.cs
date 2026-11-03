using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Audio.Tests
{
    public class SoundMathTests
    {
        private static float Db(float g) => 20f * Mathf.Log10(g);

        [TestCase(2f, 60f, 0.2f)]
        [TestCase(3f, 100f, 0.2f)]
        [TestCase(0.5f, 20f, 0f)]
        [TestCase(1f, 300f, 0.5f)]
        public void RolloffCurve_OverWholeRange_MatchesLevelWithinQuarterDecibel(float reference, float max, float fade)
        {
            var kind = SoundKind.Create(reference: reference, max: max, fadeShare: fade);
            AnimationCurve curve = KindCurves.Of(kind).Rolloff;

            for (int i = 0; i <= 4000; i++)
            {
                float d = max * i / 4000f;
                float exact = SoundMath.Level(d, kind);
                float got = curve.Evaluate(d / max);
                if (exact > 1e-3f)
                    Assert.AreEqual(0f, Db(got) - Db(exact), 0.25f, $"d {d}");
                else
                    Assert.AreEqual(exact, got, 1e-3f, $"d {d}");
            }
            Object.DestroyImmediate(kind);
        }

        [Test]
        public void ReverbCurve_SendReachesMaxAtReference_HasNoNaN()
        {
            var kind = SoundKind.Create(reference: 3f, max: 100f, reverbAtReference: 1f, reverbMax: 1f);
            AnimationCurve curve = KindCurves.Of(kind).Reverb;

            for (int i = 0; i <= 1000; i++)
                Assert.AreEqual(1f, curve.Evaluate(i / 1000f), 1e-4f);
            Object.DestroyImmediate(kind);
        }

        [Test]
        public void SpreadCompensation_MeasuredLoss_IsUndone()
        {
            Assert.AreEqual(1f, SoundMath.SpreadCompensation(0f), 1e-6f);
            Assert.AreEqual(1.28f, 20f * Mathf.Log10(SoundMath.SpreadCompensation(90f)), 1e-4f);
            Assert.AreEqual(3.04f, 20f * Mathf.Log10(SoundMath.SpreadCompensation(180f)), 1e-4f);
        }

        [Test]
        public void DistanceGain_PastReference_HalvesPerDoubling()
        {
            Assert.AreEqual(1f, SoundMath.DistanceGain(1f, 3f, 100f, 0.2f), 1e-6f);
            Assert.AreEqual(0.5f, SoundMath.DistanceGain(6f, 3f, 100f, 0.2f), 1e-6f);
            Assert.AreEqual(0.0625f, SoundMath.DistanceGain(48f, 3f, 100f, 0.2f), 1e-6f);
            Assert.AreEqual(0f, SoundMath.DistanceGain(100f, 3f, 100f, 0.2f));
        }

        [TestCase(3f, 13900f)]
        [TestCase(10f, 7600f)]
        [TestCase(40f, 3800f)]
        public void AirCutoff_AirFour_FallsAsInverseRootOfDistance(float d, float hz)
        {
            Assert.AreEqual(hz, SoundMath.AirCutoff(d, 4f), hz * 0.01f);
        }

        [Test]
        public void AirCutoff_Limits_ClampedAndOpenWithoutAir()
        {
            Assert.AreEqual(SoundMath.MaxCutoff, SoundMath.AirCutoff(0.1f, 4f));
            Assert.AreEqual(SoundMath.MinCutoff, SoundMath.AirCutoff(1e5f, 4f));
            Assert.AreEqual(SoundMath.MaxCutoff, SoundMath.AirCutoff(50f, 0f));
        }

        [Test]
        public void CutoffCurve_EveryDistance_IsCutoffOver22090()
        {
            var kind = SoundKind.Create(reference: 3f, max: 100f, air: 5f);
            AnimationCurve curve = KindCurves.Of(kind).Cutoff;

            for (int i = 0; i <= 1000; i++)
            {
                float d = 100f * i / 1000f;
                float exact = SoundMath.AirCutoff(d, 5f);
                Assert.AreEqual(exact, curve.Evaluate(d / 100f) * 22090f, exact * 0.02f, $"d {d}");
            }
            Object.DestroyImmediate(kind);
        }

        [Test]
        public void SpreadCurve_EveryDistance_StaysWithinHalf()
        {
            var kind = SoundKind.Create(reference: 3f, max: 100f, spreadNear: 180f, spreadFar: 0f, spreadFarAt: 6f);
            AnimationCurve curve = KindCurves.Of(kind).Spread;

            for (int i = 0; i <= 2000; i++)
            {
                float y = curve.Evaluate(i / 2000f);
                Assert.That(y, Is.InRange(0f, 0.5f));
            }
            Assert.AreEqual(0.5f, curve.Evaluate(0.01f), 1e-3f);
            Assert.AreEqual(0f, curve.Evaluate(0.5f), 1e-3f);
            Object.DestroyImmediate(kind);
        }

        [Test]
        public void ReverbSend_PastReference_RisesWithDistanceUpToMax()
        {
            Assert.AreEqual(0.05f, SoundMath.ReverbSend(1f, 2f, 0.05f, 1.1f), 1e-6f);
            Assert.AreEqual(0.1f, SoundMath.ReverbSend(4f, 2f, 0.05f, 1.1f), 1e-6f);
            Assert.AreEqual(0.4f, SoundMath.ReverbSend(16f, 2f, 0.05f, 1.1f), 1e-6f);
            Assert.AreEqual(1.1f, SoundMath.ReverbSend(60f, 2f, 0.05f, 1.1f), 1e-6f);
        }

        [Test]
        public void BackAmount_Directions_ZeroInFrontOneBehind()
        {
            Assert.AreEqual(0f, SoundMath.BackAmount(Vector3.forward, Vector3.forward * 5f));
            Assert.AreEqual(0f, SoundMath.BackAmount(Vector3.forward, Vector3.right), 1e-6f);
            Assert.AreEqual(1f, SoundMath.BackAmount(Vector3.forward, Vector3.back * 2f), 1e-6f);
            Assert.AreEqual(0f, SoundMath.BackAmount(Vector3.forward, Vector3.zero));
        }

        [Test]
        public void Smooth_AtTimeConstant_Covers63PercentWithoutOvershoot()
        {
            float v = 0f, t = 0f;
            while (t < 0.06f - 1e-6f)
            {
                v = SoundMath.Smooth(v, 1f, 0.001f, 0.06f);
                t += 0.001f;
            }
            Assert.AreEqual(1f - Mathf.Exp(-1f), v, 0.01f);

            for (int i = 0; i < 5000; i++)
            {
                v = SoundMath.Smooth(v, 1f, 0.05f, 0.06f);
                Assert.LessOrEqual(v, 1f);
            }
        }
    }
}
