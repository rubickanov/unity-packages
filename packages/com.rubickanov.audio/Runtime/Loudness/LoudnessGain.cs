using System;

namespace Rubickanov.Audio
{
    /// <summary>
    /// A slow gain that brings a stream to a target loudness: target − measured, clamped, with asymmetric slew — down
    /// fast (a drop lands under the limiter within a second), up slow (a quiet intro is lifted a little, without
    /// breathing). The first measured seconds slew fast both ways to land the first estimate. A guard keeps the last 3 s
    /// within <see cref="Guard"/> LU over the target, so a drop after a quiet intro is cut at once instead of as the
    /// window fills. Pure C#, audio-thread safe.
    /// </summary>
    public sealed class LoudnessGain
    {
        private float _settled;

        public LoudnessGain(float targetLufs = -18f, float minDb = -12f, float maxDb = 3f, float downDbPerSecond = 6f,
            float upDbPerSecond = 0.5f, float settleSeconds = 3f, float guardLu = 2f)
        {
            if (minDb > maxDb) throw new ArgumentException("minDb > maxDb");
            Target = targetLufs;
            MinDb = minDb;
            MaxDb = maxDb;
            Down = Math.Abs(downDbPerSecond);
            Up = Math.Abs(upDbPerSecond);
            SettleSeconds = settleSeconds;
            Guard = Math.Abs(guardLu);
            Reset();
        }

        public float Target { get; }
        public float MinDb { get; }
        public float MaxDb { get; }
        public float Down { get; }
        public float Up { get; }
        public float SettleSeconds { get; }

        /// <summary>LU the short-term loudness may stand over the target before it, not the window, sets the aim.</summary>
        public float Guard { get; }

        /// <summary>The gain applied now, dB.</summary>
        public float Db { get; private set; }

        /// <summary>The gain it moves to, dB (target − measured, clamped).</summary>
        public float Aim { get; private set; }

        public void Reset()
        {
            Db = 0f;
            Aim = 0f;
            _settled = 0f;
        }

        /// <summary>
        /// Moves the gain toward the aim for <paramref name="measuredLufs"/> (NaN: nothing measured, keep it) over
        /// <paramref name="seconds"/>. Returns the new gain, dB.
        /// </summary>
        public float Step(float measuredLufs, float seconds) => Step(measuredLufs, float.NaN, seconds);

        /// <summary>
        /// As <see cref="Step(float, float)"/>, with the short-term loudness as a guard: the aim follows whichever of
        /// <paramref name="measuredLufs"/> and <paramref name="shortTermLufs"/> − <see cref="Guard"/> is louder.
        /// </summary>
        public float Step(float measuredLufs, float shortTermLufs, float seconds)
        {
            if (!float.IsNaN(measuredLufs) && !float.IsNaN(shortTermLufs))
            {
                measuredLufs = Math.Max(measuredLufs, shortTermLufs - Guard);
            }

            // Settling counts measured time only: a stream that starts silent keeps its fast first seconds.
            bool settling = _settled < SettleSeconds;
            if (!float.IsNaN(measuredLufs))
            {
                Aim = Math.Clamp(Target - measuredLufs, MinDb, MaxDb);
                _settled += seconds;
            }

            float delta = Aim - Db;
            float rate = delta < 0f ? Down : settling ? Down : Up;
            float max = rate * seconds;
            Db += Math.Clamp(delta, -max, max);
            return Db;
        }

        /// <summary>
        /// Measures nothing: scales <paramref name="frames"/> interleaved frames by a ramp from <paramref name="fromDb"/>
        /// to the current gain, so a step between buffers never clicks.
        /// </summary>
        public void Apply(float[] data, int offset, int frames, int channels, float fromDb)
        {
            float from = Linear(fromDb), to = Linear(Db);
            if (frames <= 0) return;
            float step = (to - from) / frames;
            int i = offset;
            for (int f = 0; f < frames; f++)
            {
                float g = from + step * (f + 1);
                for (int c = 0; c < channels; c++, i++)
                {
                    data[i] *= g;
                }
            }
        }

        /// <summary>Measures with <paramref name="meter"/> (before the gain), steps and applies it in one call.</summary>
        public void Process(LoudnessMeter meter, float[] data, int offset, int frames, int channels)
        {
            meter.Add(data, offset, frames);
            float from = Db;
            Step(meter.Integrated, meter.ShortTerm, (float)frames / meter.Rate);
            Apply(data, offset, frames, channels, from);
        }

        public static float Linear(float db) => (float)Math.Pow(10.0, db / 20.0);
    }
}
