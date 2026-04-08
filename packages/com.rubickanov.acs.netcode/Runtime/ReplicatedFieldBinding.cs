using System;
using System.Collections.Generic;
using R3;
using Unity.Collections;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode
{
    internal abstract class ReplicatedFieldBinding
    {
        public bool IsDirty { get; protected set; }

        public abstract void WriteTo(FastBufferWriter writer);
        public abstract void ReadFrom(FastBufferReader reader);
        public abstract void SubscribeAsAuthority(ref DisposableBag disposables);
        public abstract void ApplyFromNetwork();
        public abstract void ClearDirty();
    }

    internal sealed class ReplicatedFieldBinding<T> : ReplicatedFieldBinding
        where T : unmanaged
    {
        private readonly ReactiveProperty<T> _reactive;
        private T _pendingValue;
        private bool _hasPendingValue;
        private bool _suppressNotification;

        public ReplicatedFieldBinding(ReactiveProperty<T> reactive)
        {
            _reactive = reactive;
        }

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

        public override void ApplyFromNetwork()
        {
            if (!_hasPendingValue) return;
            _suppressNotification = true;
            _reactive.Value = _pendingValue;
            _suppressNotification = false;
            _hasPendingValue = false;
        }

        public override void ClearDirty()
        {
            IsDirty = false;
        }
    }

    internal static class ReplicatedFieldBindingFactory
    {
        private static readonly Dictionary<Type, Type> BindingTypeCache = new();

        public static ReplicatedFieldBinding Create(object reactiveProperty, Type valueType)
        {
            if (!BindingTypeCache.TryGetValue(valueType, out var bindingType))
            {
                bindingType = typeof(ReplicatedFieldBinding<>).MakeGenericType(valueType);
                BindingTypeCache[valueType] = bindingType;
            }

            return (ReplicatedFieldBinding)Activator.CreateInstance(bindingType, reactiveProperty);
        }
    }
}
