using System;
using NUnit.Framework;

namespace Rubickanov.Audio.Tests
{
    public class TruePeakLimiterTests
    {
        private const int Rate = 48000;
        private static readonly int[] Blocks = { 1024, 37, 512, 5, 2048 };

        // Interleaved stereo x(frame, channel) through the limiter in blocks of uneven sizes.
        private static float[] Run(TruePeakLimiter limiter, Func<int, int, float> x, int frames)
        {
            var data = new float[frames * 2];
            for (int f = 0; f < frames; f++)
            {
                data[f * 2] = x(f, 0);
                data[f * 2 + 1] = x(f, 1);
            }
            int done = 0;
            for (int b = 0; done < frames; b++)
            {
                int n = Math.Min(Blocks[b % Blocks.Length], frames - done);
                limiter.Process(data.AsSpan(done * 2, n * 2), 2);
                done += n;
            }
            return data;
        }

        // True peak by 16× oversampling with a long windowed sinc (the reference, not the limiter's interpolator).
        private static float TruePeak(float[] data, int from)
        {
            const int half = 64;
            const int over = 16;
            float most = 0f;
            int frames = data.Length / 2;
            for (int c = 0; c < 2; c++)
            for (int f = from + half; f < frames - half; f++)
            for (int p = 0; p < over; p++)
            {
                double t = (double)p / over;
                double v = 0;
                for (int k = -half + 1; k <= half; k++)
                {
                    double d = k - t;
                    double sinc = Math.Abs(d) < 1e-9 ? 1 : Math.Sin(Math.PI * d) / (Math.PI * d);
                    double w = 0.5 * (1 + Math.Cos(Math.PI * d / (half + 1)));
                    v += data[(f + k) * 2 + c] * sinc * w;
                }
                most = Math.Max(most, (float)Math.Abs(v));
            }
            return most;
        }

        private static float Db(float x) => 20f * MathF.Log10(x);

        [Test]
        public void QuietSignal_PassesUnchanged_Delayed()
        {
            var limiter = new TruePeakLimiter(Rate, -1f, 1.5f, 100f);
            Func<int, int, float> x = (f, c) => 0.5f * MathF.Sin(2f * MathF.PI * 1000f * f / Rate + c);
            float[] y = Run(limiter, x, 9000);
            int lag = limiter.Latency;
            for (int f = lag; f < 9000; f++)
                Assert.AreEqual(x(f - lag, 0), y[f * 2], 1e-6f, $"frame {f}");
            Assert.AreEqual(1f, limiter.TakeLowestGain());
        }

        [Test]
        public void InterSamplePeak_IsHeldUnderTheCeiling()
        {
            // fs/4 at 45°: samples at 0.707 of the wave's peak, so the samples sit under 0 dBFS while the wave is +2 dB over.
            var limiter = new TruePeakLimiter(Rate, -1f, 1.5f, 100f);
            float[] y = Run(limiter, (f, c) => 0.9f * MathF.Sqrt(2f) * MathF.Sin(MathF.PI / 2f * f + MathF.PI / 4f), 6000);
            float tp = TruePeak(y, 2000);
            Assert.That(Db(tp), Is.LessThanOrEqualTo(-1f + 0.1f), $"true peak {Db(tp):F2} dBTP");
            Assert.That(Db(tp), Is.GreaterThan(-1.6f), "limited, not crushed");
        }

        [Test]
        public void LoudBursts_NeverPassTheCeiling_AndTheGainComesBack()
        {
            var limiter = new TruePeakLimiter(Rate, -1f, 1.5f, 50f);
            var noise = new Random(3);
            var table = new float[Rate];
            for (int i = 0; i < table.Length; i++) table[i] = (float)(noise.NextDouble() * 2 - 1);
            // 0.25 s of noise peaking near +12 dBFS with a 2 ms step in, then 0.5 s at -20 dBFS.
            Func<int, int, float> x = (f, c) => table[(f * 2 + c) % table.Length] * (f < 12000 ? 4f : 0.1f);
            float[] y = Run(limiter, x, 36000);
            float sample = 0f;
            for (int i = 0; i < y.Length; i++) sample = Math.Max(sample, Math.Abs(y[i]));
            Assert.That(Db(sample), Is.LessThanOrEqualTo(-1f + 1e-3f), "sample peak");
            // No true-peak bound here: for white noise up to Nyquist it depends on the reconstruction filter (BS.1770
            // band-limits; an ideal sinc reads ~1 dB higher). The band-limited test holds the true peak.
            Assert.That(limiter.TakeLowestGain(), Is.LessThan(0.3f));

            // 0.4 s after the burst (8 release time constants) the quiet part comes out unchanged.
            int lag = limiter.Latency;
            for (int f = 31000; f < 36000; f++)
                Assert.AreEqual(x(f - lag, 0), y[f * 2], 1e-3f, $"frame {f}");
        }

        [Test]
        public void BandLimitedLoudMix_TruePeakWithinATenthOfTheCeiling()
        {
            // Audio as it reaches the ears: 120 partials 40 Hz..17 kHz, peaking ~+8 dBFS, swelling in and out.
            var rng = new Random(5);
            var freq = new float[120];
            var phase = new float[120];
            for (int i = 0; i < freq.Length; i++)
            {
                freq[i] = 40f * MathF.Pow(17000f / 40f, (float)rng.NextDouble());
                phase[i] = (float)(rng.NextDouble() * 2 * Math.PI);
            }
            Func<int, int, float> x = (f, c) =>
            {
                float v = 0f;
                for (int i = 0; i < freq.Length; i++) v += MathF.Sin(2f * MathF.PI * freq[i] * f / Rate + phase[i] + c);
                return v * 0.12f * (1f + MathF.Sin(2f * MathF.PI * 3f * f / Rate));
            };
            var limiter = new TruePeakLimiter(Rate, -1f, 1.5f, 100f);
            float[] y = Run(limiter, x, 24000);
            Assert.That(Db(TruePeak(y, 0)), Is.LessThanOrEqualTo(-1f + 0.1f));
            Assert.That(limiter.TakeLowestGain(), Is.LessThan(0.5f), "the mix was loud enough to limit");
        }

        [Test]
        public void StereoShareOneGain()
        {
            var limiter = new TruePeakLimiter(Rate, -1f, 1.5f, 100f);
            float[] y = Run(limiter, (f, c) => (c == 0 ? 2f : 0.2f) * MathF.Sin(2f * MathF.PI * 440f * f / Rate), 20000);
            for (int f = 10000; f < 20000; f++)
                if (Math.Abs(y[f * 2]) > 0.05f)
                    Assert.AreEqual(10f, y[f * 2] / y[f * 2 + 1], 1e-3f);
        }
    }
}
