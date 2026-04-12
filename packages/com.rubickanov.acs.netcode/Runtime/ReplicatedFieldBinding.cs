using System;
using System.Collections.Generic;
using System.Linq.Expressions;
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
        protected T _pendingValue;
        protected bool _hasPendingValue;
        protected bool _suppressNotification;

        public ReplicatedFieldBinding(ReactiveProperty<T> reactive)
        {
            _reactive = reactive;
        }

        public override unsafe int Size => sizeof(T);

        public override void SubscribeAsAuthority(ref DisposableBag disposables)
        {
            _reactive.Subscribe(value =>
            {
                if (_suppressNotification) return;
                IsDirty = true;
                _ownerWroteSinceSpawn = true;
            }).AddTo(ref disposables);
        }

        public override unsafe void WriteTo(FastBufferWriter writer)
        {
            var value = _reactive.Value;
            byte* ptr = (byte*)&value;
            writer.WriteBytesSafe(ptr, sizeof(T));
        }

        public override unsafe void ReadFrom(FastBufferReader reader)
        {
            fixed (T* ptr = &_pendingValue)
            {
                reader.ReadBytesSafe((byte*)ptr, sizeof(T));
            }
            _hasPendingValue = true;
        }

        public override unsafe void Skip(FastBufferReader reader)
        {
            T discard = default;
            reader.ReadBytesSafe((byte*)&discard, sizeof(T));
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
        private static readonly Dictionary<Type, Func<object, ReplicatedFieldBinding>> FieldFactories = new();
        private static readonly Dictionary<Type, Func<object, object, ReplicatedFieldBinding>> InterpFactories = new();
        private static readonly Dictionary<Type, Func<object, object, ReplicatedFieldBinding>> AuthorityRenderFactories = new();
        private static readonly HashSet<Type> WarnedUnsupportedTypes = new();

        public static ReplicatedFieldBinding Create(object reactiveProperty, Type valueType, FieldBindingKind kind)
        {
            if (kind == FieldBindingKind.PassiveInterpolated || kind == FieldBindingKind.AuthorityRendered)
            {
                if (Interpolators.TryGetRaw(valueType, out var lerper))
                {
                    var cache = kind == FieldBindingKind.PassiveInterpolated
                        ? InterpFactories
                        : AuthorityRenderFactories;

                    if (!cache.TryGetValue(valueType, out var factory))
                    {
                        factory = kind == FieldBindingKind.PassiveInterpolated
                            ? BuildInterpFactory(valueType)
                            : BuildAuthorityRenderFactory(valueType);
                        cache[valueType] = factory;
                    }

                    return factory(reactiveProperty, lerper);
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

            return plainFactory(reactiveProperty);
        }

        private static Func<object, ReplicatedFieldBinding> BuildFieldFactory(Type valueType)
        {
            var bindingType = typeof(ReplicatedFieldBinding<>).MakeGenericType(valueType);
            var reactiveType = typeof(ReactiveProperty<>).MakeGenericType(valueType);
            var ctor = bindingType.GetConstructor(new[] { reactiveType })!;

            var param = Expression.Parameter(typeof(object), "reactive");
            var body = Expression.New(ctor, Expression.Convert(param, reactiveType));
            return Expression.Lambda<Func<object, ReplicatedFieldBinding>>(body, param).Compile();
        }

        private static Func<object, object, ReplicatedFieldBinding> BuildInterpFactory(Type valueType)
        {
            var bindingType = typeof(InterpolatedFieldBinding<>).MakeGenericType(valueType);
            return BuildLerpCtorFactory(bindingType, valueType);
        }

        private static Func<object, object, ReplicatedFieldBinding> BuildAuthorityRenderFactory(Type valueType)
        {
            var bindingType = typeof(AuthorityRenderBinding<>).MakeGenericType(valueType);
            return BuildLerpCtorFactory(bindingType, valueType);
        }

        private static Func<object, object, ReplicatedFieldBinding> BuildLerpCtorFactory(Type bindingType, Type valueType)
        {
            var reactiveType = typeof(ReactiveProperty<>).MakeGenericType(valueType);
            var lerpType = typeof(Lerp<>).MakeGenericType(valueType);
            var ctor = bindingType.GetConstructor(new[] { reactiveType, lerpType })!;

            var paramReactive = Expression.Parameter(typeof(object), "reactive");
            var paramLerper = Expression.Parameter(typeof(object), "lerper");
            var body = Expression.New(ctor,
                Expression.Convert(paramReactive, reactiveType),
                Expression.Convert(paramLerper, lerpType));
            return Expression.Lambda<Func<object, object, ReplicatedFieldBinding>>(
                body, paramReactive, paramLerper).Compile();
        }
    }
}
