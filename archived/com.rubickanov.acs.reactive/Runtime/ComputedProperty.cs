using System;
using R3;

namespace Rubickanov.ACS.Runtime.Reactive
{
    /// <summary>
    /// A read-only reactive value derived from one or more source reactive properties.
    /// Recomputes whenever any source emits and pushes the result through <see cref="Property"/>.
    /// <para/>
    /// Build instances via the <see cref="ComputedProperty"/> factory rather than the
    /// constructor. Declare them as <c>readonly</c> fields on an aspect, mark with
    /// <see cref="ComputedAttribute"/>, and wire them in the aspect constructor:
    /// <code>
    /// public class HealthAspect : IEntityAspect
    /// {
    ///     public readonly ReactiveProperty&lt;float&gt; Health = new(100f);
    ///     public readonly ReactiveProperty&lt;float&gt; MaxHealth = new(100f);
    ///
    ///     [Computed] public readonly ComputedProperty&lt;float&gt; HealthPercent;
    ///     [Computed] public readonly ComputedProperty&lt;bool&gt;  IsDead;
    ///
    ///     public HealthAspect()
    ///     {
    ///         HealthPercent = ComputedProperty.From(Health, MaxHealth, (h, max) =&gt; max &gt; 0 ? h / max : 0f);
    ///         IsDead        = ComputedProperty.From(Health, h =&gt; h &lt;= 0f);
    ///     }
    /// }
    /// </code>
    /// <b>Disposal.</b> A computed holds live subscriptions to its sources. When every source
    /// lives on the same entity as the computed, the whole graph is collected together once the
    /// entity is dropped — no explicit cleanup needed. When a source lives on a <i>different</i>
    /// entity (or on the <see cref="World"/>), that source keeps the computed — and its owner —
    /// alive; call <see cref="Dispose"/> when the owning entity is destroyed to break the link.
    /// <para/>
    /// <b>Thread safety:</b> not thread-safe, matching ACS aspects and R3 reactive properties.
    /// </summary>
    public sealed class ComputedProperty<T> : IDisposable
    {
        private readonly ReactiveProperty<T> _backing;
        private readonly IDisposable[] _sources;
        private bool _disposed;

        internal ComputedProperty(ReactiveProperty<T> backing, IDisposable[] sources)
        {
            _backing = backing;
            _sources = sources;
        }

        /// <summary>
        /// The current derived value. Equivalent to <c>Property.CurrentValue</c>, exposed
        /// directly so call sites that only read the value skip going through the observable.
        /// </summary>
        public T CurrentValue => _backing.CurrentValue;

        /// <summary>
        /// The derived value as a read-only reactive property. Subscribe to react to changes;
        /// the current value is delivered immediately on subscribe, like any R3 reactive property.
        /// </summary>
        public ReadOnlyReactiveProperty<T> Property => _backing;

        /// <summary>The derived value as a plain observable stream.</summary>
        public Observable<T> AsObservable() => _backing;

        /// <summary>Renders the current value so the default inspector / debug drawer shows it readably.</summary>
        public override string ToString() => _backing.CurrentValue?.ToString() ?? "null";

        /// <summary>
        /// Releases the source subscriptions and the backing property. Idempotent — the second
        /// call is a no-op. After disposal the computed stops recomputing.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < _sources.Length; i++)
                _sources[i].Dispose();
            _backing.Dispose();
        }
    }

    /// <summary>
    /// Factory for <see cref="ComputedProperty{T}"/>. Each <c>From</c> overload takes 1–4 source
    /// reactive properties and a selector, seeds the initial value synchronously from the sources'
    /// current values, then keeps the result in sync as any source changes.
    /// <para/>
    /// Sources are typed as <see cref="ReadOnlyReactiveProperty{T}"/> so a plain
    /// <see cref="ReactiveProperty{T}"/> (which derives from it) passes through unchanged, while
    /// the factory can read each source's <c>CurrentValue</c> for the initial seed.
    /// </summary>
    public static class ComputedProperty
    {
        public static ComputedProperty<TOut> From<T1, TOut>(
            ReadOnlyReactiveProperty<T1> source,
            Func<T1, TOut> selector)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (selector is null) throw new ArgumentNullException(nameof(selector));

            var backing = new ReactiveProperty<TOut>(selector(source.CurrentValue));
            // R3 delivers the current value on Subscribe, so the lambda fires once immediately,
            // re-seeding backing with the same value (no observers yet → no visible double-emit).
            var sub = source.Subscribe(v => backing.Value = selector(v));
            return new ComputedProperty<TOut>(backing, new[] { sub });
        }

        public static ComputedProperty<TOut> From<T1, T2, TOut>(
            ReadOnlyReactiveProperty<T1> source1,
            ReadOnlyReactiveProperty<T2> source2,
            Func<T1, T2, TOut> selector)
        {
            if (source1 is null) throw new ArgumentNullException(nameof(source1));
            if (source2 is null) throw new ArgumentNullException(nameof(source2));
            if (selector is null) throw new ArgumentNullException(nameof(selector));

            TOut Compute() => selector(source1.CurrentValue, source2.CurrentValue);
            var backing = new ReactiveProperty<TOut>(Compute());
            var s1 = source1.Subscribe(_ => backing.Value = Compute());
            var s2 = source2.Subscribe(_ => backing.Value = Compute());
            return new ComputedProperty<TOut>(backing, new[] { s1, s2 });
        }

        public static ComputedProperty<TOut> From<T1, T2, T3, TOut>(
            ReadOnlyReactiveProperty<T1> source1,
            ReadOnlyReactiveProperty<T2> source2,
            ReadOnlyReactiveProperty<T3> source3,
            Func<T1, T2, T3, TOut> selector)
        {
            if (source1 is null) throw new ArgumentNullException(nameof(source1));
            if (source2 is null) throw new ArgumentNullException(nameof(source2));
            if (source3 is null) throw new ArgumentNullException(nameof(source3));
            if (selector is null) throw new ArgumentNullException(nameof(selector));

            TOut Compute() => selector(source1.CurrentValue, source2.CurrentValue, source3.CurrentValue);
            var backing = new ReactiveProperty<TOut>(Compute());
            var s1 = source1.Subscribe(_ => backing.Value = Compute());
            var s2 = source2.Subscribe(_ => backing.Value = Compute());
            var s3 = source3.Subscribe(_ => backing.Value = Compute());
            return new ComputedProperty<TOut>(backing, new[] { s1, s2, s3 });
        }

        public static ComputedProperty<TOut> From<T1, T2, T3, T4, TOut>(
            ReadOnlyReactiveProperty<T1> source1,
            ReadOnlyReactiveProperty<T2> source2,
            ReadOnlyReactiveProperty<T3> source3,
            ReadOnlyReactiveProperty<T4> source4,
            Func<T1, T2, T3, T4, TOut> selector)
        {
            if (source1 is null) throw new ArgumentNullException(nameof(source1));
            if (source2 is null) throw new ArgumentNullException(nameof(source2));
            if (source3 is null) throw new ArgumentNullException(nameof(source3));
            if (source4 is null) throw new ArgumentNullException(nameof(source4));
            if (selector is null) throw new ArgumentNullException(nameof(selector));

            TOut Compute() => selector(source1.CurrentValue, source2.CurrentValue, source3.CurrentValue, source4.CurrentValue);
            var backing = new ReactiveProperty<TOut>(Compute());
            var s1 = source1.Subscribe(_ => backing.Value = Compute());
            var s2 = source2.Subscribe(_ => backing.Value = Compute());
            var s3 = source3.Subscribe(_ => backing.Value = Compute());
            var s4 = source4.Subscribe(_ => backing.Value = Compute());
            return new ComputedProperty<TOut>(backing, new[] { s1, s2, s3, s4 });
        }
    }
}
