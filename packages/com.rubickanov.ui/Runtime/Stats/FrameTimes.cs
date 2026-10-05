using System;

namespace Rubickanov.UI
{
    /// <summary>
    /// Frames a second and the slowest frame, over windows of <see cref="Window"/> seconds. Fed one frame time a frame,
    /// unscaled, so slow motion and a pause do not change them; NaN until the first window closes.
    /// </summary>
    public sealed class FrameTimes
    {
        private double _elapsed;
        private double _slowest;
        private int _frames;

        public FrameTimes(double window = 0.5)
        {
            if (!(window > 0.0)) throw new ArgumentOutOfRangeException(nameof(window), window, "The window must be longer than zero.");
            Window = window;
        }

        public double Window { get; }

        /// <summary>Frames a second over the last window.</summary>
        public double PerSecond { get; private set; } = double.NaN;

        /// <summary>The longest frame of the last window, in milliseconds.</summary>
        public double SlowestMs { get; private set; } = double.NaN;

        /// <summary>Takes one frame's time in seconds, such as <c>Time.unscaledDeltaTime</c>.</summary>
        public void Tick(double seconds)
        {
            if (seconds <= 0.0 || double.IsNaN(seconds))
            {
                return;
            }

            _elapsed += seconds;
            _frames++;
            _slowest = Math.Max(_slowest, seconds);
            if (_elapsed < Window)
            {
                return;
            }

            PerSecond = _frames / _elapsed;
            SlowestMs = _slowest * 1000.0;
            _elapsed = 0.0;
            _slowest = 0.0;
            _frames = 0;
        }
    }
}
