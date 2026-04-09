using System;
using System.Collections.Generic;
using R3;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    [Preserve]
    internal abstract class ReplicatedEventBinding
    {
        public AuthorityMode Authority { get; }
        public Reliability Reliability { get; }

        protected ReplicatedEventBinding(AuthorityMode authority, Reliability reliability)
        {
            Authority = authority;
            Reliability = reliability;
        }

        public abstract void SubscribeAsAuthority(ref DisposableBag disposables, byte eventIndex, Action<byte, byte[]> broadcaster);
        public abstract void ApplyFromNetwork(FastBufferReader reader);
    }

    [Preserve]
    internal sealed class ReplicatedEventBinding<T> : ReplicatedEventBinding
        where T : unmanaged
    {
        private readonly Subject<T> _subject;
        private byte _eventIndex;
        private Action<byte, byte[]>? _broadcaster;

        public ReplicatedEventBinding(Subject<T> subject, AuthorityMode authority, Reliability reliability)
            : base(authority, reliability)
        {
            _subject = subject;
        }

        public override void SubscribeAsAuthority(ref DisposableBag disposables, byte eventIndex, Action<byte, byte[]> broadcaster)
        {
            _eventIndex = eventIndex;
            _broadcaster = broadcaster;
            _subject.Subscribe(OnLocalEvent).AddTo(ref disposables);
        }

        private unsafe void OnLocalEvent(T value)
        {
            var writer = new FastBufferWriter(sizeof(T), Allocator.Temp);
            try
            {
                byte* ptr = (byte*)&value;
                writer.WriteBytesSafe(ptr, sizeof(T));
                _broadcaster!.Invoke(_eventIndex, writer.ToArray());
            }
            finally
            {
                writer.Dispose();
            }
        }

        public override unsafe void ApplyFromNetwork(FastBufferReader reader)
        {
            T value = default;
            reader.ReadBytesSafe((byte*)&value, sizeof(T));
            _subject.OnNext(value);
        }
    }

    [Preserve]
    internal static class ReplicatedEventBindingFactory
    {
        private static readonly Dictionary<Type, Type> BindingTypeCache = new();

        public static ReplicatedEventBinding Create(object subject, Type valueType, AuthorityMode authority, Reliability reliability)
        {
            if (!BindingTypeCache.TryGetValue(valueType, out var bindingType))
            {
                bindingType = typeof(ReplicatedEventBinding<>).MakeGenericType(valueType);
                BindingTypeCache[valueType] = bindingType;
            }

            return (ReplicatedEventBinding)Activator.CreateInstance(bindingType, subject, authority, reliability);
        }
    }
}
