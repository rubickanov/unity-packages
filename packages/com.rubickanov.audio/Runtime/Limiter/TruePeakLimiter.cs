using System;

namespace Rubickanov.Audio
{
    /// <summary>
    /// Lookahead brickwall limiter on true peaks: the peak between samples is estimated by 4× oversampling (a 16-tap
    /// Kaiser-windowed sinc per phase, as ITU-R BS.1770 meters do; under-reads by ≤ 0.15 dB on audio band-limited to
    /// 17 kHz at 48 kHz, ≤ 0.6 dB on white noise up to Nyquist), the gain each sample needs is held for the
    /// lookahead and ramped down over it (min-hold, then a moving average of the same length), so the gain reaches its
    /// value exactly when the peak leaves and nothing passes the ceiling; it comes back with an exponential release.
    /// Channels share one gain (the image does not shift). The output is delayed by <see cref="Latency"/> frames.
    /// <see cref="Process"/> on the audio thread; the setters from any thread. Pure C#, no allocation after the first
    /// block of a channel count.
    /// </summary>
    public sealed class TruePeakLimiter
    {
        // Interpolator: the value between frame c and c + 1 reads frames c - 7 .. c + 8.
        private const int Taps = 16;
        private const double KaiserBeta = 6.0;
        private const int Behind = Taps / 2 - 1;
        private const int Ahead = Taps / 2;
        private const int Phases = 4;

        private static readonly float[] Kernel = MakeKernel();
        // Largest sum of |taps| of a phase: |interpolated| <= this × the largest |sample| it reads.
        private static readonly float KernelGain = MeasureKernelGain();

        private readonly int _rate;
        private readonly int _window;
        private float _ceiling;
        private float _releaseCoef;

        private int _channels;
        private float[] _ring = Array.Empty<float>();
        private int _ringMask;
        private long _frame;

        // Gains each frame needs, its min-hold queue, and the released gains being averaged.
        private readonly float[] _need;
        private readonly int[] _queue;
        private int _queueHead;
        private int _queueCount;
        private readonly float[] _released;
        private double _sum;
        private float _release = 1f;
        // Interval peak between the frame being judged and the one before it.
        private float _lastInterval;
        private float _quietTail;
        private int _lastFrames;

        private float _lowest = 1f;

        /// <param name="rate">Frames per second.</param>
        /// <param name="ceilingDb">The highest true peak let through, dBTP.</param>
        /// <param name="lookaheadMs">How far ahead the gain sees a peak (the attack ramp), ms.</param>
        /// <param name="releaseMs">Time constant of the gain coming back, ms.</param>
        public TruePeakLimiter(int rate, float ceilingDb = -1f, float lookaheadMs = 1.5f, float releaseMs = 100f)
        {
            if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
            _rate = rate;
            _window = WindowFor(rate, lookaheadMs);
            _need = new float[_window];
            _queue = new int[_window];
            _released = new float[_window];
            for (int i = 0; i < _window; i++) _released[i] = 1f;
            _sum = _window;
            CeilingDb = ceilingDb;
            ReleaseMs = releaseMs;
        }

        /// <summary>Frames the output trails the input by.</summary>
        public int Latency => _window - 1 + Ahead;

        /// <summary>The <see cref="Latency"/> a limiter of this rate and lookahead has.</summary>
        public static int LatencyFor(int rate, float lookaheadMs) => WindowFor(rate, lookaheadMs) - 1 + Ahead;

        private static int WindowFor(int rate, float lookaheadMs) =>
            Math.Max(1, (int)Math.Round(Math.Max(0f, lookaheadMs) * 0.001 * rate));

        public int Rate => _rate;

        /// <summary>The highest true peak let through, dBTP.</summary>
        public float CeilingDb
        {
            get => 20f * MathF.Log10(_ceiling);
            set => _ceiling = MathF.Pow(10f, Math.Min(0f, value) / 20f);
        }

        /// <summary>Time constant of the gain coming back, ms.</summary>
        public float ReleaseMs
        {
            get => _releaseCoef >= 1f ? 0f : (float)(-1000.0 / (_rate * Math.Log(1.0 - _releaseCoef)));
            set => _releaseCoef = value <= 0f ? 1f : (float)(1.0 - Math.Exp(-1000.0 / (value * _rate)));
        }

        /// <summary>The lowest gain since the last call, then resets: 1 = the limiter did nothing. Any thread.</summary>
        public float TakeLowestGain()
        {
            float g = _lowest;
            _lowest = 1f;
            return g;
        }

        /// <summary>Limits interleaved frames in place (they come out <see cref="Latency"/> frames later).</summary>
        public void Process(Span<float> data, int channels)
        {
            if (channels <= 0 || data.Length < channels) return;
            if (channels != _channels) Reset(channels);

            int frames = data.Length / channels;
            float ceiling = _ceiling;
            float release = _releaseCoef;

            // A block whose interpolation cannot reach the ceiling needs no oversampling: the interpolator reads at
            // most Taps frames back, all in this block and the one before when both are that long.
            float loud = 0f;
            for (int i = 0; i < frames * channels; i++) loud = Math.Max(loud, Math.Abs(data[i]));
            bool quiet = frames >= Taps && _lastFrames >= Taps && Math.Max(loud, _quietTail) * KernelGain < ceiling;
            _quietTail = loud;
            _lastFrames = frames;

            // The running sum loses bits over hours: rebuild it once a block.
            double sum = 0;
            for (int i = 0; i < _window; i++) sum += _released[i];
            _sum = sum;

            float lowest = 1f;
            for (int f = 0; f < frames; f++)
            {
                long n = _frame++;
                int at = (int)(n & _ringMask) * channels;
                for (int c = 0; c < channels; c++) _ring[at + c] = data[f * channels + c];

                // Judge frame n - Ahead: its sample and both intervals around it.
                float peak = 0f;
                if (quiet)
                {
                    _lastInterval = 0f;
                }
                else
                {
                    long judged = n - Ahead;
                    float interval = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        peak = Math.Max(peak, Math.Abs(Read(judged, c)));
                        for (int p = 1; p < Phases; p++)
                        {
                            float v = 0f;
                            int k = (p - 1) * Taps;
                            for (int t = 0; t < Taps; t++) v += Kernel[k + t] * Read(judged - Behind + t, c);
                            interval = Math.Max(interval, Math.Abs(v));
                        }
                    }
                    peak = Math.Max(peak, Math.Max(interval, _lastInterval));
                    _lastInterval = interval;
                }

                float need = peak > ceiling ? ceiling / peak : 1f;

                // Min over the last _window needs (a monotonic queue of slots).
                int slot = (int)(n % _window);
                _need[slot] = need;
                if (_queueCount > 0 && _queue[_queueHead] == slot)
                {
                    _queueHead = (_queueHead + 1) % _window;
                    _queueCount--;
                }
                while (_queueCount > 0 && _need[_queue[(_queueHead + _queueCount - 1) % _window]] >= need) _queueCount--;
                _queue[(_queueHead + _queueCount) % _window] = slot;
                _queueCount++;
                float held = _need[_queue[_queueHead]];

                // Down at once, back up with the release; then averaged over the window.
                _release = Math.Min(held, _release + (1f - _release) * release);
                _sum += _release - _released[slot];
                _released[slot] = _release;
                float gain = (float)(_sum / _window);
                if (gain > 1f) gain = 1f;
                if (gain < lowest) lowest = gain;

                long outFrame = n - Latency;
                for (int c = 0; c < channels; c++)
                    data[f * channels + c] = outFrame < 0 ? 0f : Read(outFrame, c) * gain;
            }

            if (lowest < _lowest) _lowest = lowest;
        }

        /// <summary>Forgets the audio held and the gain (a silence of <see cref="Latency"/> frames follows).</summary>
        public void Reset(int channels)
        {
            _channels = Math.Max(1, channels);
            int size = 1;
            while (size < Latency + Taps + 1) size <<= 1;
            _ring = new float[size * _channels];
            _ringMask = size - 1;
            _frame = 0;
            _queueHead = 0;
            _queueCount = 0;
            for (int i = 0; i < _window; i++) _released[i] = 1f;
            _sum = _window;
            _release = 1f;
            _lastInterval = 0f;
            _quietTail = 0f;
            _lastFrames = 0;
        }

        private float Read(long frame, int channel) =>
            frame < 0 ? 0f : _ring[(int)(frame & _ringMask) * _channels + channel];

        // Phases 1..3 of a 4× Kaiser-windowed sinc interpolator, Taps each, every phase summing to 1.
        private static float[] MakeKernel()
        {
            var kernel = new float[(Phases - 1) * Taps];
            double half = Taps / 2.0 + 0.5;
            for (int p = 1; p < Phases; p++)
            {
                double frac = (double)p / Phases;
                double total = 0;
                for (int t = 0; t < Taps; t++)
                {
                    double d = t - Behind - frac;
                    double sinc = Math.Abs(d) < 1e-9 ? 1.0 : Math.Sin(Math.PI * d) / (Math.PI * d);
                    double r = d / half;
                    double window = BesselI0(KaiserBeta * Math.Sqrt(Math.Max(0.0, 1.0 - r * r))) / BesselI0(KaiserBeta);
                    kernel[(p - 1) * Taps + t] = (float)(sinc * window);
                    total += sinc * window;
                }
                for (int t = 0; t < Taps; t++) kernel[(p - 1) * Taps + t] = (float)(kernel[(p - 1) * Taps + t] / total);
            }
            return kernel;
        }

        private static double BesselI0(double x)
        {
            double sum = 1, term = 1, q = x * x / 4;
            for (int k = 1; k < 40; k++)
            {
                term *= q / (k * k);
                sum += term;
            }
            return sum;
        }

        private static float MeasureKernelGain()
        {
            float most = 1f;
            for (int p = 0; p < Phases - 1; p++)
            {
                float s = 0f;
                for (int t = 0; t < Taps; t++) s += Math.Abs(Kernel[p * Taps + t]);
                most = Math.Max(most, s);
            }
            return most;
        }
    }
}
