using UnityEngine;

namespace Rubickanov.UI
{
    /// <summary>
    /// When a held direction steps: at once on a press, then after <see cref="Delay"/> every <see cref="Interval"/>. The
    /// caller passes unscaled time, so a pause does not stop it. A new direction is a new press.
    /// </summary>
    public sealed class StepRepeat
    {
        public const float DefaultDelay = 0.4f;
        public const float DefaultInterval = 0.1f;

        /// <summary>How far a stick goes before it steps.</summary>
        public const float DeadZone = 0.5f;

        private MenuStep? _held;
        private float _nextAt;
        private bool _waitForRelease;

        public StepRepeat(float delay = DefaultDelay, float interval = DefaultInterval)
        {
            Delay = delay;
            Interval = interval;
        }

        /// <summary>Seconds a direction is held before it repeats.</summary>
        public float Delay { get; }

        /// <summary>Seconds between repeats.</summary>
        public float Interval { get; }

        /// <summary>The direction a navigate value points, along its longer axis; none inside the dead zone.</summary>
        public static MenuStep? Direction(Vector2 move)
        {
            if (move.magnitude < DeadZone) return null;
            if (Mathf.Abs(move.y) >= Mathf.Abs(move.x)) return move.y > 0f ? MenuStep.Up : MenuStep.Down;
            return move.x > 0f ? MenuStep.Right : MenuStep.Left;
        }

        /// <summary>A one-way axis (a trigger pair) past the dead zone as a step back or on; none inside it.</summary>
        public static MenuStep? Along(float value, MenuStep back, MenuStep on) =>
            Mathf.Abs(value) < DeadZone ? null : value < 0f ? back : on;

        /// <summary>The direction held now counts for nothing until it is let go; another one steps.</summary>
        public void WaitForRelease() => _waitForRelease = true;

        /// <summary>Whether the direction held at <paramref name="now"/> steps this frame, and which way.</summary>
        public bool Next(MenuStep? held, float now, out MenuStep step)
        {
            step = default;
            if (held == null)
            {
                _held = null;
                _waitForRelease = false;
                return false;
            }

            if (_waitForRelease && held == _held) return false;

            _waitForRelease = false;
            if (held != _held)
            {
                _held = held;
                _nextAt = now + Delay;
                step = held.Value;
                return true;
            }

            if (now < _nextAt) return false;

            _nextAt += Interval;
            if (_nextAt <= now) _nextAt = now + Interval;
            step = held.Value;
            return true;
        }
    }
}
