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
        protected bool _ownerWroteSinceSpawn;
        public bool OwnerWroteSinceSpawn => _ownerWroteSinceSpawn;
        public void ResetOwnerWroteSinceSpawn() => _ownerWroteSinceSpawn = false;

        public abstract void WriteTo(FastBufferWriter writer);
        // Initial-sync path: by default reuses WriteTo (correct for scalars where the
        // current value IS the full state). Collection bindings override this to emit a
        // full-state op sequence (Clear + Add*) because their normal WriteTo only drains
        // accumulated deltas since the last tick.
        public virtual void WriteSnapshotTo(FastBufferWriter writer) => WriteTo(writer);
        // SnapshotSize may exceed Size for collection bindings — a full snapshot can be
        // larger than a single-tick delta. EntityReplicator uses this to size the initial-
        // sync payload hint correctly.
        public virtual int SnapshotSize => Size;
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
        // Collection factories: keyed by element type (T for ObservableList<T>). Construction
        // takes (ObservableList<T> as object, IFieldCodec<T> as object).
        private static readonly Dictionary<Type, Func<object, object, ReplicatedFieldBinding>> ObservableListFactories = new();
        // Dictionary factories: keyed by (keyType, valueType). Construction takes
        // (ObservableDictionary<K,V> as object, IObservableDictionaryKeyCodec<K> as object,
        //  IFieldCodec<V> as object).
        private static readonly Dictionary<(Type, Type), Func<object, object, object, ReplicatedFieldBinding>> ObservableDictionaryFactories = new();
        // Phase 3 collection factories. Keyed by element type (T for ObservableHashSet<T>
        // and ObservableFixedSizeRingBuffer<T>). Construction takes
        // (collection as object, IFieldCodec<T> as object) — identical signature to
        // ObservableListFactories so the cache shapes match.
        private static readonly Dictionary<Type, Func<object, object, ReplicatedFieldBinding>> ObservableHashSetFactories = new();
        private static readonly Dictionary<Type, Func<object, object, ReplicatedFieldBinding>> ObservableRingBufferFactories = new();
        private static readonly HashSet<Type> WarnedUnsupportedTypes = new();

        // Play-Mode-without-Domain-Reload safety: clear static caches on subsystem registration.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            FieldFactories.Clear();
            InterpFactories.Clear();
            AuthorityRenderFactories.Clear();
            ObservableListFactories.Clear();
            ObservableDictionaryFactories.Clear();
            ObservableHashSetFactories.Clear();
            ObservableRingBufferFactories.Clear();
            WarnedUnsupportedTypes.Clear();
        }

        // tickDelta is only consumed by the AuthorityRendered branch (sizes coalesce / stale
        // windows). Plain and PassiveInterpolated ignore it, so callers in tests that build
        // those kinds can omit the argument.
        // quantization defaults to None so existing call sites (tests, EntityReplicator pre-attribute)
        // keep raw-memcpy behaviour. Invalid (valueType, quantization) combos throw via CodecRegistry.
        // system is required only for EntityRef-typed fields — EntityRefCodec translates via the
        // system's EntityId ↔ NetworkObjectId maps. Defaulting it to null keeps the factory
        // signature backwards-compatible for tests that round-trip primitive types.
        public static ReplicatedFieldBinding Create(
            object reactiveProperty,
            Type valueType,
            FieldBindingKind kind,
            double tickDelta = 0,
            QuantizationMode quantization = QuantizationMode.None,
            EntityReplicationSystem? system = null)
        {
            object codec;
            if (valueType == typeof(EntityRef))
            {
                if (system == null)
                    throw new InvalidOperationException(
                        "[ReplicatedFieldBindingFactory] EntityRef replication requires an EntityReplicationSystem context. " +
                        "Pass the replicator's _system when calling Create.");
                codec = system.GetOrCreateEntityRefCodec();
            }
            else
            {
                codec = CodecRegistry.Resolve(valueType, quantization);
            }

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
                    // windows track NetworkTickSystem.TickRate.
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
                        $"[EntityReplicator] InterpolationMode.Linear is set on a field of type '{valueType.Name}', " +
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

        // Collection factory. Mirrors the plain-scalar path — same ConstructorInfo.Invoke
        // pattern, different generic binding type. Keeping it parallel to Create() (rather
        // than an overload that tries to dispatch by type at runtime) so the scalar
        // hot-path stays untouched by the collection extension.
        public static ReplicatedFieldBinding CreateObservableList(
            object observableList,
            Type elementType,
            QuantizationMode quantization = QuantizationMode.None,
            EntityReplicationSystem? system = null)
        {
            object codec;
            if (elementType == typeof(EntityRef))
            {
                if (system == null)
                    throw new InvalidOperationException(
                        "[ReplicatedFieldBindingFactory] ObservableList<EntityRef> replication requires an EntityReplicationSystem context. " +
                        "Pass the replicator's _system when calling CreateObservableList.");
                codec = system.GetOrCreateEntityRefCodec();
            }
            else
            {
                codec = CodecRegistry.Resolve(elementType, quantization);
            }

            if (!ObservableListFactories.TryGetValue(elementType, out var factory))
            {
                factory = BuildObservableListFactory(elementType);
                ObservableListFactories[elementType] = factory;
            }
            return factory(observableList, codec);
        }

        private static Func<object, object, ReplicatedFieldBinding> BuildObservableListFactory(Type elementType)
        {
            var bindingType = typeof(ObservableListBinding<>).MakeGenericType(elementType);
            var listType = typeof(ObservableCollections.ObservableList<>).MakeGenericType(elementType);
            var codecType = typeof(IFieldCodec<>).MakeGenericType(elementType);
            var ctor = bindingType.GetConstructor(new[] { listType, codecType })
                ?? throw new InvalidOperationException($"No (ObservableList<T>, IFieldCodec<T>) ctor on {bindingType}.");
            return (list, codec) => (ReplicatedFieldBinding)ctor.Invoke(new[] { list, codec });
        }

        // Dictionary factory. Parallel to CreateObservableList — resolves key codec
        // (StringKeyCodec for string; UnmanagedKeyCodec<K> wrapping RawCodec<K> otherwise)
        // and value codec (EntityRefCodec / CodecRegistry), then dispatches to the cached
        // ConstructorInfo.Invoke delegate for the closed generic binding.
        public static ReplicatedFieldBinding CreateObservableDictionary(
            object observableDict,
            Type keyType,
            Type valueType,
            QuantizationMode quantization = QuantizationMode.None,
            EntityReplicationSystem? system = null)
        {
            // Value codec — identical to scalar / list resolution.
            object valueCodec;
            if (valueType == typeof(EntityRef))
            {
                if (system == null)
                    throw new InvalidOperationException(
                        "[ReplicatedFieldBindingFactory] ObservableDictionary<K,EntityRef> replication requires an EntityReplicationSystem context. " +
                        "Pass the replicator's _system when calling CreateObservableDictionary.");
                valueCodec = system.GetOrCreateEntityRefCodec();
            }
            else
            {
                valueCodec = CodecRegistry.Resolve(valueType, quantization);
            }

            // Key codec — string goes through the local StringKeyCodec singleton;
            // unmanaged keys wrap a RawCodec<K> in UnmanagedKeyCodec<K>. Quantization
            // is intentionally NOT applied to keys — scanner validates value-only.
            object keyCodec;
            if (keyType == typeof(string))
            {
                keyCodec = StringKeyCodec.Instance;
            }
            else
            {
                var innerKeyCodec = CodecRegistry.Resolve(keyType, QuantizationMode.None);
                var wrapperType = typeof(UnmanagedKeyCodec<>).MakeGenericType(keyType);
                var wrapperCodecType = typeof(IFieldCodec<>).MakeGenericType(keyType);
                var wrapperCtor = wrapperType.GetConstructor(new[] { wrapperCodecType })
                    ?? throw new InvalidOperationException($"No (IFieldCodec<T>) ctor on {wrapperType}.");
                keyCodec = wrapperCtor.Invoke(new[] { innerKeyCodec });
            }

            if (!ObservableDictionaryFactories.TryGetValue((keyType, valueType), out var factory))
            {
                factory = BuildObservableDictionaryFactory(keyType, valueType);
                ObservableDictionaryFactories[(keyType, valueType)] = factory;
            }
            return factory(observableDict, keyCodec, valueCodec);
        }

        private static Func<object, object, object, ReplicatedFieldBinding> BuildObservableDictionaryFactory(Type keyType, Type valueType)
        {
            var bindingType = typeof(ObservableDictionaryBinding<,>).MakeGenericType(keyType, valueType);
            var dictType = typeof(ObservableCollections.ObservableDictionary<,>).MakeGenericType(keyType, valueType);
            var keyCodecType = typeof(IObservableDictionaryKeyCodec<>).MakeGenericType(keyType);
            var valueCodecType = typeof(IFieldCodec<>).MakeGenericType(valueType);
            var ctor = bindingType.GetConstructor(new[] { dictType, keyCodecType, valueCodecType })
                ?? throw new InvalidOperationException($"No (ObservableDictionary<K,V>, IObservableDictionaryKeyCodec<K>, IFieldCodec<V>) ctor on {bindingType}.");
            return (dict, keyCodec, valueCodec) => (ReplicatedFieldBinding)ctor.Invoke(new[] { dict, keyCodec, valueCodec });
        }

        // HashSet factory — mirrors CreateObservableList. Same codec resolution rules
        // (EntityRef → system codec, otherwise CodecRegistry.Resolve) and the same
        // ConstructorInfo.Invoke pattern for IL2CPP safety.
        public static ReplicatedFieldBinding CreateObservableHashSet(
            object observableHashSet,
            Type elementType,
            QuantizationMode quantization = QuantizationMode.None,
            EntityReplicationSystem? system = null)
        {
            object codec;
            if (elementType == typeof(EntityRef))
            {
                if (system == null)
                    throw new InvalidOperationException(
                        "[ReplicatedFieldBindingFactory] ObservableHashSet<EntityRef> replication requires an EntityReplicationSystem context. " +
                        "Pass the replicator's _system when calling CreateObservableHashSet.");
                codec = system.GetOrCreateEntityRefCodec();
            }
            else
            {
                codec = CodecRegistry.Resolve(elementType, quantization);
            }

            if (!ObservableHashSetFactories.TryGetValue(elementType, out var factory))
            {
                factory = BuildObservableHashSetFactory(elementType);
                ObservableHashSetFactories[elementType] = factory;
            }
            return factory(observableHashSet, codec);
        }

        private static Func<object, object, ReplicatedFieldBinding> BuildObservableHashSetFactory(Type elementType)
        {
            var bindingType = typeof(ObservableHashSetBinding<>).MakeGenericType(elementType);
            var setType = typeof(ObservableCollections.ObservableHashSet<>).MakeGenericType(elementType);
            var codecType = typeof(IFieldCodec<>).MakeGenericType(elementType);
            var ctor = bindingType.GetConstructor(new[] { setType, codecType })
                ?? throw new InvalidOperationException($"No (ObservableHashSet<T>, IFieldCodec<T>) ctor on {bindingType}.");
            return (set, codec) => (ReplicatedFieldBinding)ctor.Invoke(new[] { set, codec });
        }

        // Ring-buffer factory — fixed-size variant only. Plain ObservableRingBuffer<T>
        // is rejected at scan time.
        public static ReplicatedFieldBinding CreateObservableRingBuffer(
            object observableRingBuffer,
            Type elementType,
            QuantizationMode quantization = QuantizationMode.None,
            EntityReplicationSystem? system = null)
        {
            object codec;
            if (elementType == typeof(EntityRef))
            {
                if (system == null)
                    throw new InvalidOperationException(
                        "[ReplicatedFieldBindingFactory] ObservableFixedSizeRingBuffer<EntityRef> replication requires an EntityReplicationSystem context. " +
                        "Pass the replicator's _system when calling CreateObservableRingBuffer.");
                codec = system.GetOrCreateEntityRefCodec();
            }
            else
            {
                codec = CodecRegistry.Resolve(elementType, quantization);
            }

            if (!ObservableRingBufferFactories.TryGetValue(elementType, out var factory))
            {
                factory = BuildObservableRingBufferFactory(elementType);
                ObservableRingBufferFactories[elementType] = factory;
            }
            return factory(observableRingBuffer, codec);
        }

        private static Func<object, object, ReplicatedFieldBinding> BuildObservableRingBufferFactory(Type elementType)
        {
            var bindingType = typeof(ObservableRingBufferBinding<>).MakeGenericType(elementType);
            var bufferType = typeof(ObservableCollections.ObservableFixedSizeRingBuffer<>).MakeGenericType(elementType);
            var codecType = typeof(IFieldCodec<>).MakeGenericType(elementType);
            var ctor = bindingType.GetConstructor(new[] { bufferType, codecType })
                ?? throw new InvalidOperationException($"No (ObservableFixedSizeRingBuffer<T>, IFieldCodec<T>) ctor on {bindingType}.");
            return (buffer, codec) => (ReplicatedFieldBinding)ctor.Invoke(new[] { buffer, codec });
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
