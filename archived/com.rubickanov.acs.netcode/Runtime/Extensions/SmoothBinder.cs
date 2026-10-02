using System;
using R3;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Registers a per-frame binding that reads <see cref="ReactivePropertyExtensions.Smooth{T}"/>
    /// and forwards the value to a setter. Removes the per-component <c>LateUpdate</c> that
    /// manual <c>transform.position = _movement.Position.Smooth()</c> call-sites would
    /// otherwise need — all bindings tick from a single shared driver.
    /// <para>
    /// Dispose the returned <see cref="IDisposable"/> to unregister; intended for use with
    /// <see cref="DisposableBag"/> / <c>AddTo</c> so subscriptions and smooth bindings share
    /// the same lifecycle in <c>OnSubscribe</c>.
    /// </para>
    /// <para>
    /// The setter is invoked every rendered frame, even if the underlying
    /// <see cref="ReactiveProperty{T}.Value"/> did not change — smoothing is a continuous
    /// value in time, not a discrete event. Use <c>Subscribe</c> when you want "on change"
    /// semantics; use <c>Bind</c> when you want "current smoothed value, every frame".
    /// </para>
    /// </summary>
    [Preserve]
    public static class SmoothBinder
    {
        /// <summary>
        /// Begin driving <paramref name="setter"/> once per frame with
        /// <paramref name="source"/>.Smooth(). Dispose to unregister.
        /// </summary>
        public static IDisposable Bind<T>(ReactiveProperty<T> source, Action<T> setter)
            where T : unmanaged
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            var binding = new SmoothBinding<T>(source, setter);
            SmoothDriver.Register(binding);
            return binding;
        }
    }

    internal interface ISmoothBinding
    {
        void Tick();
    }

    internal sealed class SmoothBinding<T> : ISmoothBinding, IDisposable where T : unmanaged
    {
        private readonly ReactiveProperty<T> _source;
        private readonly Action<T> _setter;
        private bool _disposed;

        public SmoothBinding(ReactiveProperty<T> source, Action<T> setter)
        {
            _source = source;
            _setter = setter;
        }

        public void Tick()
        {
            // Defensive: list iteration in the driver may still hold a reference to a
            // disposed binding in the same frame as Dispose (e.g. disposed inside a sibling
            // binding's setter). Skip the setter rather than fire a stale write.
            if (_disposed) return;
            _setter(_source.Smooth());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SmoothDriver.Unregister(this);
        }
    }
}
