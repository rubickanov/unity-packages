using System;
using System.Collections.Generic;

namespace Rubickanov.Report
{
    /// <summary>
    /// The frame rate as Sentry metrics: the game feeds every frame's time, and every <see cref="Interval"/> seconds
    /// (and when reports stop or the game quits) one point goes out: <c>game.fps.avg</c>, <c>game.fps.low1</c> (the
    /// frame rate of the 99th percentile frame time) and <c>game.frame.ms</c>, with the attributes of the moment. A
    /// window with frames out of focus is dropped. Main thread only.
    /// </summary>
    public sealed class FrameSampler
    {
        public const string Avg = "game.fps.avg";
        public const string Low1 = "game.fps.low1";
        public const string FrameMs = "game.frame.ms";

        private const float BinMs = 0.25f;
        private const int Bins = 2000;

        private readonly int[] _bins = new int[Bins + 1];
        private readonly Func<List<KeyValuePair<string, object>>> _attributes;
        private int _frames;
        private double _seconds;
        private bool _unfocused;

        public FrameSampler(float interval, Func<List<KeyValuePair<string, object>>> attributes)
        {
            Interval = interval;
            _attributes = attributes;
        }

        public float Interval { get; set; }

        /// <summary>Points sent so far.</summary>
        public int Sent { get; private set; }

        /// <summary>Counts a frame; sends a point once the window is full.</summary>
        public void Tick(float deltaSeconds, bool focused)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            _unfocused |= !focused;
            _frames++;
            _seconds += deltaSeconds;
            int bin = (int)(deltaSeconds * 1000f / BinMs);
            _bins[bin < Bins ? bin : Bins]++;
            if (_seconds >= Interval)
            {
                Emit();
            }
        }

        /// <summary>The window so far: average and 1%-low frame rate, the average frame time in milliseconds.</summary>
        public (double Avg, double Low1, double FrameMs, int Frames) Measure()
        {
            if (_frames == 0 || _seconds <= 0)
            {
                return (0, 0, 0, 0);
            }

            int over = (int)Math.Ceiling(_frames * 0.01);
            int seen = 0;
            int at = Bins;
            for (int i = Bins; i >= 0; i--)
            {
                seen += _bins[i];
                if (seen >= over)
                {
                    at = i;
                    break;
                }
            }

            double slowMs = (at + 1) * BinMs;
            return (_frames / _seconds, 1000.0 / slowMs, _seconds * 1000.0 / _frames, _frames);
        }

        /// <summary>Sends the window as a point now (unless it saw the game out of focus) and starts a new one.</summary>
        public bool Emit()
        {
            (double avg, double low1, double ms, int frames) = Measure();
            bool send = frames > 0 && !_unfocused && Sentry.Unity.SentrySdk.IsEnabled;
            if (send)
            {
                List<KeyValuePair<string, object>> attributes = _attributes?.Invoke() ?? new List<KeyValuePair<string, object>>();
                Sentry.Unity.SentrySdk.Metrics.EmitGauge(Avg, avg, "none", attributes, null);
                Sentry.Unity.SentrySdk.Metrics.EmitGauge(Low1, low1, "none", attributes, null);
                Sentry.Unity.SentrySdk.Metrics.EmitDistribution(FrameMs, ms, "millisecond", attributes, null);
                Sent++;
            }

            Array.Clear(_bins, 0, _bins.Length);
            _frames = 0;
            _seconds = 0;
            _unfocused = false;
            return send;
        }
    }
}
