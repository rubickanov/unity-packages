using System;
using NUnit.Framework;

namespace Rubickanov.Audio.Tests
{
    public class PropagationDelayTests
    {
        private const int Rate = 48000;

        // Runs the delay over a mono source x(frame) in blocks of the given sizes; aim(block) sets the aim before each.
        private static float[] Run(PropagationDelay delay, Func<long, float> x, int total, int[] blocks,
            Func<int, long, float?> aim = null, Func<int, long> shift = null, long start = 100000)
        {
            var output = new float[total];
            long cursor = start;
            int done = 0;
            for (int b = 0; done < total; b++)
            {
                int n = Math.Min(blocks[b % blocks.Length], total - done);
                float? a = aim?.Invoke(b, cursor);
                if (a.HasValue) delay.Aim(a.Value);
                long s = shift?.Invoke(b) ?? 0;
                if (s != 0 && delay.Shift(s, 0.01f)) cursor += s;
                delay.Plan(cursor, n, out long first, out int count);
                var window = new float[count];
                for (int i = 0; i < count; i++) window[i] = x(first + i);
                delay.Render(window, first, output.AsSpan(done, n), 1);
                cursor += n;
                done += n;
            }

            return output;
        }

        private static float Ramp(long f) => f * 1e-3f;

        // The source frame each output frame read, from the ramp x = f / 1000.
        private static double[] ReadAt(float[] y)
        {
            var at = new double[y.Length];
            for (int i = 0; i < y.Length; i++) at[i] = y[i] * 1000.0;
            return at;
        }

        [Test]
        public void SteadyDelay_WithinOneSample()
        {
            var delay = new PropagationDelay(Rate);
            delay.Aim(0.0291f);
            float[] y = Run(delay, Ramp, 8000, new[] { 512, 300, 1024 });
            double[] at = ReadAt(y);
            double want = 0.0291 * Rate;
            for (int i = 0; i < y.Length; i++)
            {
                Assert.AreEqual(100000 + i - want, at[i], 1.0, $"frame {i}");
            }
        }

        [Test]
        public void MovingDelay_ReadRateWithinSlew()
        {
            var delay = new PropagationDelay(Rate);
            // Walks 0 → 45 ms (under the jump) and back: slewed all the way, never crossfaded.
            delay.Aim(0f);
            float[] y = Run(delay, Ramp, Rate * 40, new[] { 1024, 256 },
                (b, c) => b == 2 ? 0.045f : b == 1200 ? 0f : (float?)null);
            double[] at = ReadAt(y);
            // Over 1000-frame spans: a float ramp this long is only good to ~0.2 frame per sample.
            double low = 1, high = 1;
            for (int i = 1000; i < at.Length; i += 250)
            {
                double rate = (at[i] - at[i - 1000]) / 1000;
                low = Math.Min(low, rate);
                high = Math.Max(high, rate);
            }

            Assert.GreaterOrEqual(low, 1 - 0.003 - 1e-3, "slowest read");
            Assert.LessOrEqual(high, 1 + 0.003 + 1e-3, "fastest read");
            Assert.Less(low, 0.999, "it did slew");
            Assert.AreEqual(100000 + at.Length - 1 - 0.0, at[^1], 1.0, "back at zero delay");
        }

        [Test]
        public void LagPastJump_Crossfaded()
        {
            var delay = new PropagationDelay(Rate);
            delay.Aim(0.01f);
            bool faded = false;
            float[] y = Run(delay, Ramp, Rate / 2, new[] { 480 }, (b, c) =>
            {
                if (b == 10) return 0.2f;
                return null;
            });
            // Steady again after the 20 ms crossfade, at the new delay to the sample.
            double[] at = ReadAt(y);
            int after = 11 * 480 + 960 + 10;
            for (int i = after; i < at.Length; i++)
            {
                Assert.AreEqual(100000 + i - 0.2 * Rate, at[i], 1.0, $"frame {i}");
            }

            // Mid-fade the ramp reads between the two positions (a mix, not a cut).
            double mid = at[10 * 480 + 480];
            faded = mid < 100000 + 10 * 480 + 480 - 0.01 * Rate - 1 && mid > 100000 + 10 * 480 + 480 - 0.2 * Rate + 1;
            Assert.IsTrue(faded, $"mid-fade read {mid}");
        }

        [Test]
        public void Sine_NoStepLargerThanItsOwn()
        {
            const double hz = 1000;
            Func<long, float> sine = f => (float)Math.Sin(2 * Math.PI * hz * f / Rate);
            double own = 2 * Math.Sin(Math.PI * hz / Rate);
            var delay = new PropagationDelay(Rate);
            delay.Aim(0.03f);
            var rng = new Random(7);
            // Slews, jumps both ways and resync shifts, over odd block sizes.
            float[] y = Run(delay, sine, Rate * 6, new[] { 1024, 333, 256, 700 },
                (b, c) => b % 40 == 5 ? (float)(rng.NextDouble() * 0.3) : b % 13 == 3 ? (float)(rng.NextDouble() * 0.04) : (float?)null,
                b => b % 57 == 20 ? rng.Next(-2000, 2000) : 0);
            double worst = 0;
            for (int i = 1; i < y.Length; i++) worst = Math.Max(worst, Math.Abs(y[i] - y[i - 1]));
            Assert.LessOrEqual(worst, own * 1.05, $"largest step {worst:0.0000} vs the sine's {own:0.0000}");
        }

        [Test]
        public void Shift_DuringFade_Refused()
        {
            var delay = new PropagationDelay(Rate);
            delay.Aim(0f);
            Run(delay, Ramp, 1024, new[] { 1024 });
            Assert.IsTrue(delay.Shift(500, 0.01f));
            Assert.IsFalse(delay.Shift(500, 0.01f));
        }

        [Test]
        public void Hermite_ExactOnQuadratic()
        {
            Func<float, float> p = t => 0.75f * t * t - 2 * t + 1;
            for (float t = 0; t <= 1; t += 0.125f)
            {
                Assert.AreEqual(p(t), PropagationDelay.Hermite(p(-1), p(0), p(1), p(2), t), 1e-5f);
            }
        }
    }
}
