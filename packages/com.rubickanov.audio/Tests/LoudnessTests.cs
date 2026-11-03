using System;
using NUnit.Framework;

namespace Rubickanov.Audio.Tests
{
    public class LoudnessTests
    {
        private const int Rate = 48000;

        // Stereo sine, the same on both channels, peak at dbfs.
        private static void Tone(LoudnessMeter meter, float dbfs, float seconds, float hz = 1000f, LoudnessGain gain = null)
        {
            float a = (float)Math.Pow(10, dbfs / 20.0);
            var buffer = new float[1024 * 2];
            int frames = (int)(seconds * Rate);
            long t = (long)(meter.Seconds * Rate);
            for (int done = 0; done < frames; done += 1024)
            {
                int n = Math.Min(1024, frames - done);
                for (int f = 0; f < n; f++, t++)
                {
                    float s = a * (float)Math.Sin(2 * Math.PI * hz * t / Rate);
                    buffer[2 * f] = s;
                    buffer[2 * f + 1] = s;
                }

                if (gain != null) gain.Process(meter, buffer, 0, n, 2);
                else meter.Add(buffer, 0, n);
            }
        }

        [Test]
        public void Meter_Ebu3341Case1_ReadsMinus23()
        {
            var meter = new LoudnessMeter(Rate, 2);
            Tone(meter, -23f, 20f);
            Assert.AreEqual(-23f, meter.Integrated, 0.1f);
            Assert.AreEqual(-23f, meter.Momentary, 0.1f);
        }

        [Test]
        public void Meter_SixDbStep_FollowsOnceTheWindowHasPassed()
        {
            var meter = new LoudnessMeter(Rate, 2);
            Tone(meter, -29f, 10f);
            Assert.AreEqual(-29f, meter.Integrated, 0.1f);
            Tone(meter, -23f, 2f);
            // Old blocks are within 10 LU: the window mixes them.
            Assert.That(meter.Integrated, Is.InRange(-28.5f, -23.5f));
            Tone(meter, -23f, 19f);
            Assert.AreEqual(-23f, meter.Integrated, 0.1f);
        }

        [Test]
        public void Meter_ShortTerm_IsTheLastThreeSeconds()
        {
            var meter = new LoudnessMeter(Rate, 2);
            Tone(meter, -29f, 10f);
            Tone(meter, -23f, 3f);
            Assert.AreEqual(-23f, meter.ShortTerm, 0.1f);
            Assert.That(meter.Integrated, Is.LessThan(-25f));
        }

        [Test]
        public void Gain_Guard_CutsALoudPartBeforeTheWindowFills()
        {
            var gain = new LoudnessGain(-18f, -12f, 3f, 6f, 0.5f, 0f, 3f);
            for (int i = 0; i < 20; i++) gain.Step(-30f, -10f, 0.1f);
            // Short-term −10 stands 8 LU over the target: the aim treats it as −13, 3 LU over.
            Assert.AreEqual(-5f, gain.Aim, 1e-4f);
            gain.Step(-30f, -25f, 0.1f);
            Assert.AreEqual(3f, gain.Aim, 1e-4f);
        }

        [Test]
        public void Meter_Silence_StaysNaNAndIsGatedOut()
        {
            var meter = new LoudnessMeter(Rate, 2);
            Tone(meter, -200f, 2f);
            Assert.IsNaN(meter.Integrated);
            Tone(meter, -23f, 3f);
            Tone(meter, -200f, 3f);
            // Blocks across the two edges are partly silent but within the relative gate.
            Assert.AreEqual(-23f, meter.Integrated, 0.5f);
        }

        [Test]
        public void Gain_DownFastUpSlow_AfterSettling()
        {
            var gain = new LoudnessGain(-18f, -12f, 3f, 6f, 0.5f, 3f);
            for (int i = 0; i < 400; i++) gain.Step(-18f, 0.01f);
            Assert.AreEqual(0f, gain.Db, 1e-4f);

            // 6 dB louder: −6 dB within 1 s at 6 dB/s.
            gain.Step(-12f, 0.5f);
            Assert.AreEqual(-3f, gain.Db, 1e-4f);
            gain.Step(-12f, 0.5f);
            Assert.AreEqual(-6f, gain.Db, 1e-4f);

            // Back to quiet: up at 0.5 dB/s only.
            gain.Step(-18f, 1f);
            Assert.AreEqual(-5.5f, gain.Db, 1e-4f);
            for (int i = 0; i < 100; i++) gain.Step(-18f, 0.1f);
            Assert.AreEqual(-0.5f, gain.Db, 1e-3f);
            for (int i = 0; i < 20; i++) gain.Step(-18f, 0.1f);
            Assert.AreEqual(0f, gain.Db, 1e-4f);
        }

        [Test]
        public void Gain_FirstSeconds_SlewFastBothWays()
        {
            var gain = new LoudnessGain(-18f, -12f, 3f, 6f, 0.5f, 3f);
            gain.Step(-21f, 0.25f);
            Assert.AreEqual(1.5f, gain.Db, 1e-4f);
        }

        [Test]
        public void Gain_BoostAndCut_AreCapped()
        {
            var gain = new LoudnessGain(-18f, -12f, 3f, 6f, 0.5f, 3f);
            for (int i = 0; i < 100; i++) gain.Step(-40f, 0.1f);
            Assert.AreEqual(3f, gain.Db, 1e-4f);
            for (int i = 0; i < 100; i++) gain.Step(0f, 0.1f);
            Assert.AreEqual(-12f, gain.Db, 1e-4f);
        }

        [Test]
        public void Gain_NothingMeasured_KeepsItsAim()
        {
            var gain = new LoudnessGain();
            for (int i = 0; i < 50; i++) gain.Step(-12f, 0.1f);
            float db = gain.Db;
            gain.Step(float.NaN, 1f);
            Assert.AreEqual(db, gain.Db, 1e-4f);
        }

        [Test]
        public void Process_LoudToneLandsOnTargetWithoutSteps()
        {
            var meter = new LoudnessMeter(Rate, 2);
            var gain = new LoudnessGain(-18f);
            Tone(meter, -10f, 6f, 1000f, gain);
            Assert.AreEqual(-8f, gain.Db, 0.05f);
        }
    }
}
