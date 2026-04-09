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

        public abstract void WriteTo(FastBufferWriter writer);
        public abstract void ReadFrom(FastBufferReader reader);
        public abstract void Skip(FastBufferReader reader);
        public abstract void SubscribeAsAuthority(ref DisposableBag disposables);
        public abstract void ApplyFromNetwork(double receivedTime);
        public virtual void TickRender(double renderTime) { }
        public abstract void ClearDirty();

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

    [Preserve]
    internal static class ReplicatedFieldBindingFactory
    {
        private static readonly Dictionary<Type, Type> BindingTypeCache = new();
        private static readonly Dictionary<Type, Type> InterpolatedBindingTypeCache = new();
        private static readonly HashSet<Type> WarnedUnsupportedTypes = new();

        public static ReplicatedFieldBinding Create(object reactiveProperty, Type valueType, bool interpolate)
        {
            if (interpolate)
            {
                if (Interpolators.TryGetRaw(valueType, out var lerper))
                {
                    if (!InterpolatedBindingTypeCache.TryGetValue(valueType, out var interpBindingType))
                    {
                        interpBindingType = typeof(InterpolatedFieldBinding<>).MakeGenericType(valueType);
                        InterpolatedBindingTypeCache[valueType] = interpBindingType;
                    }

                    return (ReplicatedFieldBinding)Activator.CreateInstance(interpBindingType, reactiveProperty, lerper);
                }

                if (WarnedUnsupportedTypes.Add(valueType))
                {
                    Debug.LogWarning(
                        $"[AspectReplicator] InterpolationMode.Linear is set on a field of type '{valueType.Name}', " +
                        $"but no lerper is registered for this type. Falling back to immediate apply. " +
                        $"Supported: float, double, Vector2, Vector3, Vector4, Quaternion, Color.");
                }
            }

            if (!BindingTypeCache.TryGetValue(valueType, out var bindingType))
            {
                bindingType = typeof(ReplicatedFieldBinding<>).MakeGenericType(valueType);
                BindingTypeCache[valueType] = bindingType;
            }

            return (ReplicatedFieldBinding)Activator.CreateInstance(bindingType, reactiveProperty);
        }
    }
}
