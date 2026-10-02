namespace Rubickanov.UI
{
    /// <summary>
    /// Turns wheel deltas into steps: one per notch of a mouse wheel, however many small events a touchpad's gesture
    /// sends, none for a sideways or empty event; a turn the other way starts over. Feed it a <c>WheelEvent</c>'s
    /// <c>delta.y</c>.
    /// </summary>
    public sealed class WheelSteps
    {
        /// <summary>A mouse wheel's notch in UI Toolkit's wheel delta.</summary>
        public const float DefaultNotch = 3f;

        private readonly float _notch;
        private float _sum;

        public WheelSteps(float notch = DefaultNotch) => _notch = notch;

        /// <summary>
        /// Takes one wheel event's delta; true when it completes a step, with <paramref name="direction"/> 1 for a turn
        /// down (towards the end of a list) and -1 for a turn up.
        /// </summary>
        public bool Take(float delta, out int direction)
        {
            direction = 0;
            if (delta == 0f) return false;

            // A turn the other way drops what the first way had gathered.
            if (_sum * delta < 0f) _sum = 0f;

            _sum += delta;
            if (_sum > -_notch && _sum < _notch) return false;

            direction = _sum > 0f ? 1 : -1;
            _sum = 0f;
            return true;
        }

        /// <summary>Drops what was gathered: the list under the wheel changed.</summary>
        public void Reset() => _sum = 0f;
    }
}
