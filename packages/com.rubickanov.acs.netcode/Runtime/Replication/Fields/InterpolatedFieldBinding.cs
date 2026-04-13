using R3;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Replicated field binding that smooths incoming snapshots via linear interpolation.
    /// Used on non-authority peers for fields marked with <see cref="InterpolationMode.Linear"/>.
    /// Snapshots are buffered by server-tick timestamp; <see cref="TickRender"/> lerps at a render
    /// time slightly behind the newest snapshot (≈2 ticks) so there is always a pair to interpolate
    /// between. The interpolated result is stored in <see cref="InterpolatedValue"/> and exposed
    /// via <see cref="ReactivePropertyExtensions.Smooth{T}"/>; the underlying
    /// <see cref="ReactiveProperty{T}.Value"/> always holds the latest raw (non-interpolated) value.
    /// </summary>
    [Preserve]
    internal sealed class InterpolatedFieldBinding<T> : ReplicatedFieldBinding<T>, IInterpolatedBinding<T>
        where T : unmanaged
    {
        private const int BufferCapacity = 32;

        private struct Snapshot
        {
            public double Time;
            public T Value;
        }

        private readonly Lerp<T> _lerp;
        private readonly Snapshot[] _buffer = new Snapshot[BufferCapacity];
        private int _head;   // next write slot
        private int _count;  // valid entries in the buffer, up to BufferCapacity
        private T _interpolatedValue;

        // Cache of the last "lower" sample index returned by TickRender. renderTime is
        // monotonic, so the lower index only ever advances forward (toward newer). We
        // also store the sample's Time at the moment of caching — if the ring wraps and
        // overwrites the slot, the stored Time will no longer match and we fall back to
        // a full newest→oldest walk. -1 means "no cached position".
        private int _lastLowerIdx = -1;
        private double _lastLowerTime;

        public override bool IsInterpolated => true;
        public T InterpolatedValue => _interpolatedValue;

        public InterpolatedFieldBinding(ReactiveProperty<T> reactive, Lerp<T> lerp)
            : base(reactive)
        {
            _lerp = lerp;
            InterpolationRegistry<T>.Register(reactive, this);
        }

        public override void ApplyFromNetwork(double receivedTime)
        {
            if (!_hasPendingValue) return;

            bool firstSnapshot = _count == 0;
            PushSnapshot(receivedTime, _pendingValue);

            // Always write the raw value so .Value holds the latest network state.
            WriteSuppressed(_pendingValue);

            // Bootstrap interpolated value on first snapshot so .Smooth() does not
            // return default(T) for the duration of the interpolation delay.
            if (firstSnapshot)
                _interpolatedValue = _pendingValue;

            _hasPendingValue = false;
        }

        public override void TickRender(double renderTime)
        {
            if (_count == 0) return;

            int newestIdx = (_head - 1 + BufferCapacity) % BufferCapacity;

            if (_count == 1)
            {
                _interpolatedValue = _buffer[newestIdx].Value;
                return;
            }

            int oldestIdx = (_head - _count + BufferCapacity) % BufferCapacity;

            // Render time ran past the newest sample — hold newest (no extrapolation).
            if (renderTime >= _buffer[newestIdx].Time)
            {
                _interpolatedValue = _buffer[newestIdx].Value;
                _lastLowerIdx = -1; // out-of-range: invalidate cache, next in-range call re-seeds
                return;
            }

            // Render time is before the oldest sample — hold oldest.
            if (renderTime <= _buffer[oldestIdx].Time)
            {
                _interpolatedValue = _buffer[oldestIdx].Value;
                _lastLowerIdx = -1;
                return;
            }

            // Find a starting lower-bound index. If the cache is valid, use it — that
            // saves the newest→oldest walk on steady-state monotonic renderTime calls.
            // Otherwise, fall back to the full walk to seed the cache.
            int lowerIdx;
            if (_lastLowerIdx >= 0
                && _buffer[_lastLowerIdx].Time == _lastLowerTime
                && _lastLowerTime <= renderTime)
            {
                lowerIdx = _lastLowerIdx;
            }
            else
            {
                lowerIdx = -1;
                for (int i = 1; i < _count; i++)
                {
                    int older = (newestIdx - i + BufferCapacity) % BufferCapacity;
                    if (_buffer[older].Time <= renderTime)
                    {
                        lowerIdx = older;
                        break;
                    }
                }

                if (lowerIdx < 0)
                {
                    // Defensive fallback (bounds checks above should make this unreachable).
                    _interpolatedValue = _buffer[oldestIdx].Value;
                    _lastLowerIdx = -1;
                    return;
                }
            }

            // Advance forward (toward newer) while the next sample is still ≤ renderTime.
            // On steady-state monotonic calls the loop runs 0–1 times; across a tick it
            // advances at most by the number of new snapshots that arrived since last call.
            int upperIdx = (lowerIdx + 1) % BufferCapacity;
            while (lowerIdx != newestIdx && _buffer[upperIdx].Time <= renderTime)
            {
                lowerIdx = upperIdx;
                upperIdx = (lowerIdx + 1) % BufferCapacity;
            }

            double span = _buffer[upperIdx].Time - _buffer[lowerIdx].Time;
            float alpha = span > 1e-9 ? (float)((renderTime - _buffer[lowerIdx].Time) / span) : 0f;
            _interpolatedValue = _lerp(_buffer[lowerIdx].Value, _buffer[upperIdx].Value, alpha);

            _lastLowerIdx = lowerIdx;
            _lastLowerTime = _buffer[lowerIdx].Time;
        }

        public override void OnDespawn()
        {
            InterpolationRegistry<T>.Unregister(_reactive);
        }

        public override void ClearInterpolationState()
        {
            _head = 0;
            _count = 0;
            _interpolatedValue = default;
            _lastLowerIdx = -1;
        }

        private void PushSnapshot(double time, T value)
        {
            _buffer[_head].Time = time;
            _buffer[_head].Value = value;
            _head = (_head + 1) % BufferCapacity;
            if (_count < BufferCapacity) _count++;
        }
    }
}
