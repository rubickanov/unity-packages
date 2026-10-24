using System;
using System.Threading;

namespace Rubickanov.Audio
{
    /// <summary>
    /// The time sound takes through the air, for a source whose samples the game owns (a PCM reader): its read position
    /// trails the reference cursor by distance / <see cref="SoundMath.SoundSpeed"/>. The delay moves toward its aim with
    /// the read rate kept within 1 ± <see cref="MaxSlew"/> (no audible pitch bend); a lag past <see cref="Jump"/> (a
    /// vehicle, a teleport) is closed by a crossfade between the old and the new read instead. Fractional reads by a
    /// 4-point Hermite. <see cref="Aim"/> on any thread; the rest on the audio thread. Pure C#.
    /// </summary>
    public sealed class PropagationDelay
    {
        // Hermite reads one frame before and two after the position.
        private const int Before = 1;
        private const int After = 2;

        private readonly int _rate;
        private readonly double _slew;
        private readonly double _jumpFrames;
        private readonly int _crossfadeFrames;
        private readonly double _maxFrames;
        private int _aimBits;
        private volatile bool _snap = true;
        private double _delay;
        private double _target;
        private double _from;
        private int _fadeLeft;
        private int _fadeLength;
        private long _cursor = long.MinValue;
        private int _frames;

        /// <param name="rate">Frames per second of the source.</param>
        /// <param name="maxSlew">Read-rate cap: 0.003 ≈ 5 cents, under the pitch JND of music.</param>
        /// <param name="jump">A lag between the aim and the delay larger than this, s, is crossfaded, not slewed.</param>
        /// <param name="crossfade">Length of that crossfade, s.</param>
        /// <param name="maxDelay">The longest delay, s (the source's history must hold it).</param>
        public PropagationDelay(int rate, float maxSlew = 0.003f, float jump = 0.05f, float crossfade = 0.02f,
            float maxDelay = 1f)
        {
            if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
            _rate = rate;
            _slew = Math.Clamp(maxSlew, 0f, 0.5f);
            _jumpFrames = Math.Max(0f, jump) * rate;
            _crossfadeFrames = Math.Max(1, (int)Math.Round(Math.Max(0f, crossfade) * rate));
            _maxFrames = Math.Max(0f, maxDelay) * rate;
        }

        public int Rate => _rate;
        public float MaxSlew => (float)_slew;
        public float Jump => (float)(_jumpFrames / _rate);
        public float Crossfade => (float)_crossfadeFrames / _rate;
        public float MaxDelay => (float)(_maxFrames / _rate);

        /// <summary>The delay asked for, s.</summary>
        public float Aimed => BitConverter.Int32BitsToSingle(Volatile.Read(ref _aimBits));

        /// <summary>The delay the last block ended on, s (the audio thread's; approximate from another thread).</summary>
        public float Current => (float)(Volatile.Read(ref _delay) / _rate);

        /// <summary>A crossfade (a jump or a <see cref="Shift"/>) is under way.</summary>
        public bool Fading => _fadeLeft > 0;

        /// <summary>Any thread: the delay to move to, s (distance / speed of sound), clamped to [0, max].</summary>
        public void Aim(float seconds)
        {
            float s = float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, MaxDelay) : 0f;
            Volatile.Write(ref _aimBits, BitConverter.SingleToInt32Bits(s));
        }

        /// <summary>The next block takes the aim at once, without slew or crossfade (the source was silent).</summary>
        public void Snap() => _snap = true;

        /// <summary>
        /// The reference cursor has just jumped by <paramref name="frames"/> (a resync): the old read fades out over
        /// <paramref name="crossfade"/> s while the new one fades in. False, and nothing changes, while another
        /// crossfade is under way: the caller retries on a later block.
        /// </summary>
        public bool Shift(long frames, float crossfade)
        {
            if (_fadeLeft > 0) return false;
            if (frames == 0) return true;
            _from = _delay + frames;
            _fadeLength = _fadeLeft = Math.Max(1, (int)Math.Round(crossfade * _rate));
            return true;
        }

        /// <summary>
        /// Plans the next block of <paramref name="frames"/> output frames whose undelayed reference starts at
        /// <paramref name="cursor"/>: the source frames <see cref="Render"/> will read are
        /// [<paramref name="first"/>, <paramref name="first"/> + <paramref name="count"/>).
        /// </summary>
        public void Plan(long cursor, int frames, out long first, out int count)
        {
            _cursor = cursor;
            _frames = frames;
            _target = Math.Min(Aimed * (double)_rate, _maxFrames);
            if (_snap)
            {
                _snap = false;
                _delay = _target;
                _fadeLeft = 0;
            }
            else if (_fadeLeft == 0 && Math.Abs(_target - _delay) > _jumpFrames)
            {
                _from = _delay;
                _delay = _target;
                _fadeLength = _fadeLeft = _crossfadeFrames;
            }

            double move = frames * _slew;
            double most = Math.Max(_delay, _target) + move;
            double least = Math.Min(_delay, _target) - move;
            if (_fadeLeft > 0)
            {
                most = Math.Max(most, _from);
                least = Math.Min(least, _from);
            }

            first = cursor - (long)Math.Ceiling(most) - Before;
            long last = cursor + frames - (long)Math.Floor(least) + After;
            count = (int)(last - first);
        }

        /// <summary>
        /// Renders the planned block into <paramref name="output"/> (interleaved, <paramref name="channels"/>) from
        /// <paramref name="window"/>: the source's frames from <paramref name="windowFirst"/> on, interleaved, covering at
        /// least what <see cref="Plan"/> asked for.
        /// </summary>
        public void Render(ReadOnlySpan<float> window, long windowFirst, Span<float> output, int channels)
        {
            if (_cursor == long.MinValue) throw new InvalidOperationException("Render without Plan");
            int frames = Math.Min(_frames, output.Length / channels);
            int available = window.Length / channels;
            double baseAt = _cursor - windowFirst;
            for (int i = 0; i < frames; i++)
            {
                double step = Math.Clamp(_target - _delay, -_slew, _slew);
                _delay += step;
                double at = baseAt + i - _delay;
                if (_fadeLeft > 0)
                {
                    double atFrom = baseAt + i - _from;
                    // Raised cosine: the old read goes 1 → 0, the new 0 → 1, their sum stays 1.
                    double w = 0.5 - 0.5 * Math.Cos(Math.PI * _fadeLeft / _fadeLength);
                    for (int c = 0; c < channels; c++)
                    {
                        float now = Sample(window, available, channels, c, at);
                        float old = Sample(window, available, channels, c, atFrom);
                        output[i * channels + c] = (float)(now * (1 - w) + old * w);
                    }

                    _fadeLeft--;
                }
                else
                {
                    for (int c = 0; c < channels; c++)
                    {
                        output[i * channels + c] = Sample(window, available, channels, c, at);
                    }
                }
            }

            Volatile.Write(ref _delay, _delay);
            _cursor = long.MinValue;
        }

        /// <summary>4-point, 3rd-order Hermite between x0 (t = 0) and x1 (t = 1).</summary>
        public static float Hermite(float xm1, float x0, float x1, float x2, float t)
        {
            float c1 = 0.5f * (x1 - xm1);
            float c2 = xm1 - 2.5f * x0 + 2f * x1 - 0.5f * x2;
            float c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
            return ((c3 * t + c2) * t + c1) * t + x0;
        }

        // Channel c at fractional frame `at` of the window; silence outside it.
        private static float Sample(ReadOnlySpan<float> window, int available, int channels, int c, double at)
        {
            long i = (long)Math.Floor(at);
            float t = (float)(at - i);
            return Hermite(At(window, available, channels, c, i - 1), At(window, available, channels, c, i),
                At(window, available, channels, c, i + 1), At(window, available, channels, c, i + 2), t);
        }

        private static float At(ReadOnlySpan<float> window, int available, int channels, int c, long frame) =>
            frame >= 0 && frame < available ? window[(int)frame * channels + c] : 0f;
    }
}
