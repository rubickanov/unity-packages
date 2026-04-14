using System;
using System.Collections.Generic;
using R3;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    [Preserve]
    internal abstract class ReplicatedFieldBinding
    {
        public bool IsDirty { get; protected set; }
        public virtual bool IsInterpolated => false;
        public abstract int Size { get; }

        // True once the authority side has written to the underlying reactive after
        // the most recent ResetOwnerWroteSinceSpawn (i.e. after spawn or after gaining
        // ownership). Owner-auth initial-sync uses this to detect whether a pure-client
        // owner has already produced a local value; if it has, incoming server state
        // for that field must be ignored to avoid overwriting the fresh local write.
        // See ISSUES.md #19.
        protected bool _ownerWroteSinceSpawn;
        public bool OwnerWroteSinceSpawn => _ownerWroteSinceSpawn;
        public void ResetOwnerWroteSinceSpawn() => _ownerWroteSinceSpawn = false;

        public abstract void WriteTo(FastBufferWriter writer);
        public abstract void ReadFrom(FastBufferReader reader);
        public abstract void Skip(FastBufferReader reader);
        public abstract void SubscribeAsAuthority(ref DisposableBag disposables);
        // Subscribe a passive sampler that feeds local writes into the binding's render-smoothing
        // state WITHOUT marking the field dirty. Used by predicted-owner fields: the owner writes
        // locally via Simulate but is NOT the replication authority, so dirty would mis-trigger a
        // relay. Default is no-op — only AuthorityRenderBinding overrides it.
        public virtual void SubscribeForLocalSampling(ref DisposableBag disposables) { }
        public abstract void ApplyFromNetwork(double receivedTime);
        public virtual void TickRender(double renderTime) { }
        public abstract void ClearDirty();
        public virtual void OnDespawn() { }
        public virtual void ClearInterpolationState() { }

        public void MarkDirty() => IsDirty = true;
    }

    [Preserve]
    internal class ReplicatedFieldBinding<T> : ReplicatedFieldBinding
        where T : unmanaged
    {
        protected readonly ReactiveProperty<T> _reactive;
        // Per-field wire codec (raw memcpy by default; quantized for [Replicated(Quantization=...)]).
        // Resolved by ReplicatedFieldBindingFactory via CodecRegistry. Always non-null —
        // factory injects RawCodec<T> for the no-quantization case.
        protected readonly IFieldCodec<T> _codec;
        protected T _pendingValue;
        protected bool _hasPendingValue;
        protected bool _suppressNotification;

        public ReplicatedFieldBinding(ReactiveProperty<T> reactive, IFieldCodec<T> codec)
        {
            _reactive = reactive;
            _codec = codec;
        }

        public override int Size => _codec.Size;

        public override void SubscribeAsAuthority(ref DisposableBag disposables)
        {
            _reactive.Subscribe(value =>
            {
                if (_suppressNotification) return;
                IsDirty = true;
                _ownerWroteSinceSpawn = true;
            }).AddTo(ref disposables);
        }

        public override void WriteTo(FastBufferWriter writer)
        {
            _codec.Write(writer, _reactive.Value);
        }

        public override void ReadFrom(FastBufferReader reader)
        {
            _pendingValue = _codec.Read(reader);
            _hasPendingValue = true;
        }

        public override void Skip(FastBufferReader reader)
        {
            // Decoded value is discarded — we just need the reader position to advance by
            // exactly Size bytes, and the codec is the single source of truth on that.
            _codec.Read(reader);
        }

        public override void ApplyFromNetwork(double receivedTime)
        {
            if (!_hasPendingValue) return;
            WriteSuppressed(_pendingValue);
            _hasPendingValue = false;
        }

        public override void ClearDirty()
        {
            IsDirty = false;
        }

        protected void WriteSuppressed(T value)
        {
            _suppressNotification = true;
            _reactive.Value = value;
            _suppressNotification = false;
        }
    }

    // Selects which binding flavour the factory produces for a given field.
    //   Plain                – no smoothing; state round-trips through ReactiveProperty.Value.
    //   PassiveInterpolated  – non-authority peers receiving network snapshots; buffers them in
    //                          InterpolatedFieldBinding and lerps behind ≈2 ticks of server time.
    //   AuthorityRendered    – peers that WRITE the field locally each tick (authority, or
    //                          predicted-owner). Samples local writes into AuthorityRenderBinding
    //                          and lerps via wall-clock between the two most recent writes so
    //                          frame-rate rendering doesn't staircase on tick-rate updates.
    internal enum FieldBindingKind
    {
        Plain,
        PassiveInterpolated,
        AuthorityRendered,
    }

    [Preserve]
    internal static class ReplicatedFieldBindingFactory
    {
        // Factory delegates take the codec (boxed IFieldCodec<T>) as the last argument. Codec
        // is resolved by Create() via CodecRegistry from the (valueType, quantization) pair.
        private static readonly Dictionary<Type, Func<object, object, ReplicatedFieldBinding>> FieldFactories = new();
        private static readonly Dictionary<Type, Func<object, object, object, ReplicatedFieldBinding>> InterpFactories = new();
        private static readonly Dictionary<Type, Func<object, object, double, object, ReplicatedFieldBinding>> AuthorityRenderFactories = new();
        private static readonly HashSet<Type> WarnedUnsupportedTypes = new();

        // Play-Mode-without-Domain-Reload safety (ISSUES.md #17 / TODO.md Batch 8).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            FieldFactories.Clear();
            InterpFactories.Clear();
            AuthorityRenderFactories.Clear();
            WarnedUnsupportedTypes.Clear();
        }

        // tickDelta is only consumed by the AuthorityRendered branch (sizes coalesce / stale
        // windows, see ISSUES.md #23). Plain and PassiveInterpolated ignore it, so callers in
        // tests that build those kinds can omit the argument.
        // quantization defaults to None so existing call sites (tests, AspectReplicator pre-attribute)
        // keep raw-memcpy behaviour. Invalid (valueType, quantization) combos throw via CodecRegistry.
        public static ReplicatedFieldBinding Create(
            object reactiveProperty,
            Type valueType,
            FieldBindingKind kind,
            double tickDelta = 0,
            QuantizationMode quantization = QuantizationMode.None)
        {
            object codec = CodecRegistry.Resolve(valueType, quantization);

            if (kind == FieldBindingKind.PassiveInterpolated || kind == FieldBindingKind.AuthorityRendered)
            {
                if (Interpolators.TryGetRaw(valueType, out var lerper))
                {
                    if (kind == FieldBindingKind.PassiveInterpolated)
                    {
                        if (!InterpFactories.TryGetValue(valueType, out var interpFactory))
                        {
                            interpFactory = BuildInterpFactory(valueType);
                            InterpFactories[valueType] = interpFactory;
                        }
                        return interpFactory(reactiveProperty, lerper, codec);
                    }

                    // AuthorityRendered: tickDelta-parameterised ctor so coalesce / stale
                    // windows track NetworkTickSystem.TickRate (see ISSUES.md #23).
                    if (!AuthorityRenderFactories.TryGetValue(valueType, out var renderFactory))
                    {
                        renderFactory = BuildAuthorityRenderFactory(valueType);
                        AuthorityRenderFactories[valueType] = renderFactory;
                    }
                    return renderFactory(reactiveProperty, lerper, tickDelta, codec);
                }

                if (WarnedUnsupportedTypes.Add(valueType))
                {
                    Debug.LogWarning(
                        $"[AspectReplicator] InterpolationMode.Linear is set on a field of type '{valueType.Name}', " +
                        $"but no lerper is registered for this type. Falling back to immediate apply. " +
                        $"Supported: float, double, Vector2, Vector3, Vector4, Quaternion, Color.");
                }
            }

            if (!FieldFactories.TryGetValue(valueType, out var plainFactory))
            {
                plainFactory = BuildFieldFactory(valueType);
                FieldFactories[valueType] = plainFactory;
            }

            return plainFactory(reactiveProperty, codec);
        }

        // Cache ConstructorInfo rather than compiled delegates. ConstructorInfo.Invoke
        // is IL2CPP-safe for closed generic types whose ctors are preserved either by
        // AotHints.UsedOnlyForAOTCodeGeneration or by user link.xml entries; Expression
        // .Lambda.Compile() is not (no runtime IL emitter on IL2CPP).
        private static Func<object, object, ReplicatedFieldBinding> BuildFieldFactory(Type valueType)
        {
            var bindingType = typeof(ReplicatedFieldBinding<>).MakeGenericType(valueType);
            var reactiveType = typeof(ReactiveProperty<>).MakeGenericType(valueType);
            var codecType = typeof(IFieldCodec<>).MakeGenericType(valueType);
            var ctor = bindingType.GetConstructor(new[] { reactiveType, codecType })
                ?? throw new InvalidOperationException($"No (ReactiveProperty<T>, IFieldCodec<T>) ctor on {bindingType}.");
            return (reactive, codec) => (ReplicatedFieldBinding)ctor.Invoke(new[] { reactive, codec });
        }

        private static Func<object, object, object, ReplicatedFieldBinding> BuildInterpFactory(Type valueType)
        {
            var bindingType = typeof(InterpolatedFieldBinding<>).MakeGenericType(valueType);
            var reactiveType = typeof(ReactiveProperty<>).MakeGenericType(valueType);
            var lerpType = typeof(Lerp<>).MakeGenericType(valueType);
            var codecType = typeof(IFieldCodec<>).MakeGenericType(valueType);
            var ctor = bindingType.GetConstructor(new[] { reactiveType, lerpType, codecType })
                ?? throw new InvalidOperationException($"No (ReactiveProperty<T>, Lerp<T>, IFieldCodec<T>) ctor on {bindingType}.");
            return (reactive, lerper, codec) => (ReplicatedFieldBinding)ctor.Invoke(new[] { reactive, lerper, codec });
        }

        private static Func<object, object, double, object, ReplicatedFieldBinding> BuildAuthorityRenderFactory(Type valueType)
        {
            var bindingType = typeof(AuthorityRenderBinding<>).MakeGenericType(valueType);
            var reactiveType = typeof(ReactiveProperty<>).MakeGenericType(valueType);
            var lerpType = typeof(Lerp<>).MakeGenericType(valueType);
            var codecType = typeof(IFieldCodec<>).MakeGenericType(valueType);
            var ctor = bindingType.GetConstructor(new[] { reactiveType, lerpType, typeof(double), codecType })
                ?? throw new InvalidOperationException($"No (ReactiveProperty<T>, Lerp<T>, double, IFieldCodec<T>) ctor on {bindingType}.");
            return (reactive, lerper, tickDelta, codec) => (ReplicatedFieldBinding)ctor.Invoke(new object[] { reactive, lerper, tickDelta, codec });
        }
    }
}
