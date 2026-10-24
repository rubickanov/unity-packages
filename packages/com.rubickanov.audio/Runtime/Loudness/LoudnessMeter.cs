using System;

namespace Rubickanov.Audio
{
    /// <summary>
    /// ITU-R BS.1770 loudness of an interleaved stream: K-weighting, 400 ms blocks every 100 ms, gated (−70 LUFS
    /// absolute, −10 LU relative) over a sliding window instead of the whole programme. Every channel weighs 1 (mono and
    /// stereo). Pure C#, no allocation after construction: safe on the audio thread.
    /// </summary>
    public sealed class LoudnessMeter
    {
        private const double AbsoluteGate = -70.0;
        private const double RelativeGate = -10.0;
        private const int HopsPerBlock = 4;
        private const int HopsShortTerm = 30;

        private readonly int _channels;
        private readonly int _hopFrames;
        private readonly Biquad[] _shelf;
        private readonly Biquad[] _highPass;
        // Mean square (summed over channels) of each 100 ms hop, a ring over the window.
        private readonly double[] _hops;
        private int _hopCount;
        private int _hopNext;
        private int _frameInHop;
        private double _sum;

        public LoudnessMeter(int rate, int channels, float windowSeconds = 20f)
        {
            if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            Rate = rate;
            _channels = channels;
            _hopFrames = Math.Max(1, rate / 10);
            _hops = new double[Math.Max(HopsShortTerm, (int)Math.Round(windowSeconds * 10f))];
            _shelf = new Biquad[channels];
            _highPass = new Biquad[channels];
            Biquad shelf = Biquad.Shelf(rate), highPass = Biquad.HighPass(rate);
            for (int c = 0; c < channels; c++)
            {
                _shelf[c] = shelf;
                _highPass[c] = highPass;
            }

            Reset();
        }

        public int Rate { get; }

        /// <summary>Gated loudness over the window, LUFS; NaN until the first block above the absolute gate.</summary>
        public float Integrated { get; private set; }

        /// <summary>The last 400 ms block, LUFS; NaN before the first block.</summary>
        public float Momentary { get; private set; }

        /// <summary>The last 3 s, ungated, LUFS (EBU R128 short-term); NaN before the first block.</summary>
        public float ShortTerm { get; private set; }

        /// <summary>Seconds of sound measured since the last reset.</summary>
        public double Seconds { get; private set; }

        public void Reset()
        {
            for (int c = 0; c < _channels; c++)
            {
                _shelf[c].Clear();
                _highPass[c].Clear();
            }

            Array.Clear(_hops, 0, _hops.Length);
            _hopCount = 0;
            _hopNext = 0;
            _frameInHop = 0;
            _sum = 0;
            Integrated = float.NaN;
            Momentary = float.NaN;
            ShortTerm = float.NaN;
            Seconds = 0;
        }

        /// <summary>Measures <paramref name="frames"/> interleaved frames of <paramref name="data"/> from <paramref name="offset"/>.</summary>
        public void Add(float[] data, int offset, int frames)
        {
            int i = offset;
            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < _channels; c++, i++)
                {
                    double y = _highPass[c].Step(_shelf[c].Step(data[i]));
                    _sum += y * y;
                }

                if (++_frameInHop == _hopFrames)
                {
                    EndHop();
                }
            }

            Seconds += (double)frames / Rate;
        }

        private void EndHop()
        {
            _hops[_hopNext] = _sum / _hopFrames;
            _hopNext = (_hopNext + 1) % _hops.Length;
            _hopCount = Math.Min(_hopCount + 1, _hops.Length);
            _sum = 0;
            _frameInHop = 0;
            if (_hopCount < HopsPerBlock)
            {
                return;
            }

            Momentary = (float)Lufs(Block(0));
            int recent = Math.Min(_hopCount, HopsShortTerm);
            double shortSum = 0;
            for (int h = 0; h < recent; h++)
            {
                shortSum += _hops[((_hopNext - 1 - h) % _hops.Length + _hops.Length) % _hops.Length];
            }

            ShortTerm = (float)Lufs(shortSum / recent);

            // Blocks ending at each hop of the window (the oldest three hops start no full block).
            int blocks = _hopCount - HopsPerBlock + 1;
            double sum = 0;
            int count = 0;
            for (int b = 0; b < blocks; b++)
            {
                double z = Block(b);
                if (Lufs(z) > AbsoluteGate)
                {
                    sum += z;
                    count++;
                }
            }

            if (count == 0)
            {
                Integrated = float.NaN;
                return;
            }

            double gate = Lufs(sum / count) + RelativeGate;
            sum = 0;
            count = 0;
            for (int b = 0; b < blocks; b++)
            {
                double z = Block(b);
                double l = Lufs(z);
                if (l > AbsoluteGate && l > gate)
                {
                    sum += z;
                    count++;
                }
            }

            Integrated = (float)Lufs(sum / count);
        }

        // Mean square of the block ending `back` hops before the newest one.
        private double Block(int back)
        {
            double z = 0;
            int end = _hopNext - 1 - back;
            for (int h = 0; h < HopsPerBlock; h++)
            {
                int at = ((end - h) % _hops.Length + _hops.Length) % _hops.Length;
                z += _hops[at];
            }

            return z / HopsPerBlock;
        }

        private static double Lufs(double meanSquare) => -0.691 + 10.0 * Math.Log10(meanSquare + 1e-20);

        // Direct form I, double precision.
        private struct Biquad
        {
            private double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            public double Step(double x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1;
                _x1 = x;
                _y2 = _y1;
                _y1 = y;
                return y;
            }

            public void Clear() => _x1 = _x2 = _y1 = _y2 = 0;

            // BS.1770 stage 1 (head shelf) for any rate, as derived in libebur128.
            public static Biquad Shelf(int rate)
            {
                const double f0 = 1681.974450955533, gainDb = 3.999843853973347, q = 0.7071752369554196;
                double k = Math.Tan(Math.PI * f0 / rate);
                double vh = Math.Pow(10.0, gainDb / 20.0);
                double vb = Math.Pow(vh, 0.4996667741545416);
                double a0 = 1.0 + k / q + k * k;
                return new Biquad
                {
                    _b0 = (vh + vb * k / q + k * k) / a0,
                    _b1 = 2.0 * (k * k - vh) / a0,
                    _b2 = (vh - vb * k / q + k * k) / a0,
                    _a1 = 2.0 * (k * k - 1.0) / a0,
                    _a2 = (1.0 - k / q + k * k) / a0,
                };
            }

            // BS.1770 stage 2 (RLB high-pass).
            public static Biquad HighPass(int rate)
            {
                const double f0 = 38.13547087602444, q = 0.5003270373238773;
                double k = Math.Tan(Math.PI * f0 / rate);
                double a0 = 1.0 + k / q + k * k;
                return new Biquad
                {
                    _b0 = 1.0,
                    _b1 = -2.0,
                    _b2 = 1.0,
                    _a1 = 2.0 * (k * k - 1.0) / a0,
                    _a2 = (1.0 - k / q + k * k) / a0,
                };
            }
        }
    }
}
