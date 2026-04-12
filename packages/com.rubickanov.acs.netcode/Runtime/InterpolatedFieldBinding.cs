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

        public override bool IsInterpolated => true;
        public T InterpolatedValue => _interpolatedValue;

        public InterpolatedFieldBinding(ReactiveProperty<T> reactive, Lerp<T> lerp)
            : base(reactive)
        {
            _lerp = lerp;
            InterpolationRegistry.Register(reactive, this);
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
                return;
            }

            // Render time is before the oldest sample — hold oldest.
            if (renderTime <= _buffer[oldestIdx].Time)
            {
                _interpolatedValue = _buffer[oldestIdx].Value;
                return;
            }

            // Walk newest → oldest to find the first older sample whose time ≤ renderTime.
            int newer = newestIdx;
            for (int i = 1; i < _count; i++)
            {
                int older = (newestIdx - i + BufferCapacity) % BufferCapacity;
                if (_buffer[older].Time <= renderTime)
                {
                    double span = _buffer[newer].Time - _buffer[older].Time;
                    float alpha = span > 1e-9 ? (float)((renderTime - _buffer[older].Time) / span) : 0f;
                    _interpolatedValue = _lerp(_buffer[older].Value, _buffer[newer].Value, alpha);
                    return;
                }
                newer = older;
            }

            // Fallback (should not be reached given the oldest bounds check above).
            _interpolatedValue = _buffer[oldestIdx].Value;
        }

        public override void OnDespawn()
        {
            InterpolationRegistry.Unregister(_reactive);
        }

        public override void ClearInterpolationState()
        {
            _head = 0;
            _count = 0;
            _interpolatedValue = default;
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
