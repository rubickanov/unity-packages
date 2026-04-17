using System;
using System.Collections.Generic;
using System.Reflection;
using R3;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    [Preserve]
    internal abstract class ReplicatedEventBinding
    {
        public AuthorityMode Authority { get; }
        public Reliability Reliability { get; }
        public abstract int PayloadSize { get; }

        protected ReplicatedEventBinding(AuthorityMode authority, Reliability reliability)
        {
            Authority = authority;
            Reliability = reliability;
        }

        public abstract void SubscribeAsAuthority(ref DisposableBag disposables, byte eventIndex,
            IEventBroadcaster broadcaster, ulong networkObjectId, bool isOwnerSubmit);
        public abstract void ApplyFromNetwork(FastBufferReader reader);
        // Symmetric to ReplicatedFieldBinding.OnDespawn. Default no-op — subscribe-side
        // teardown is covered by the DisposableBag passed into SubscribeAsAuthority, so
        // stateless bindings have nothing to do here. Introduced for future bindings that
        // carry lifecycle state beyond the Subscribe handle (e.g. if a binding ever owned
        // a pooled buffer or native allocation).
        public virtual void OnDespawn() { }
    }

    [Preserve]
    internal sealed class ReplicatedEventBinding<T> : ReplicatedEventBinding
        where T : unmanaged
    {
        private readonly Subject<T> _subject;
        private byte _eventIndex;
        private IEventBroadcaster? _broadcaster;
        private ulong _networkObjectId;
        private bool _isOwnerSubmit;

        public ReplicatedEventBinding(Subject<T> subject, AuthorityMode authority, Reliability reliability)
            : base(authority, reliability)
        {
            _subject = subject;
        }

        public override unsafe int PayloadSize => sizeof(T);

        public override void SubscribeAsAuthority(ref DisposableBag disposables, byte eventIndex,
            IEventBroadcaster broadcaster, ulong networkObjectId, bool isOwnerSubmit)
        {
            _eventIndex = eventIndex;
            _broadcaster = broadcaster;
            _networkObjectId = networkObjectId;
            _isOwnerSubmit = isOwnerSubmit;
            _subject.Subscribe(OnLocalEvent).AddTo(ref disposables);
        }

        private unsafe void OnLocalEvent(T value)
        {
            var writer = new FastBufferWriter(sizeof(ulong) + sizeof(byte) + sizeof(T), Allocator.Temp);
            try
            {
                writer.WriteValueSafe(_networkObjectId);
                writer.WriteValueSafe(_eventIndex);
                byte* ptr = (byte*)&value;
                writer.WriteBytesSafe(ptr, sizeof(T));
                _broadcaster!.SendEvent(_networkObjectId, _eventIndex, writer,
                    Authority, Reliability, _isOwnerSubmit);
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
        private static readonly Dictionary<Type, Func<object, AuthorityMode, Reliability, ReplicatedEventBinding>> Factories = new();

        // Play-Mode-without-Domain-Reload safety: clear static caches on subsystem registration.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Factories.Clear();
        }

        public static ReplicatedEventBinding Create(object subject, Type valueType, AuthorityMode authority, Reliability reliability)
        {
            if (!Factories.TryGetValue(valueType, out var factory))
            {
                factory = BuildFactory(valueType);
                Factories[valueType] = factory;
            }

            return factory(subject, authority, reliability);
        }

        // Cache ConstructorInfo rather than compiled delegates. ConstructorInfo.Invoke
        // is IL2CPP-safe for closed generic types whose ctors are preserved either by
        // AotHints.UsedOnlyForAOTCodeGeneration or by user link.xml entries; Expression
        // .Lambda.Compile() is not (no runtime IL emitter on IL2CPP).
        private static Func<object, AuthorityMode, Reliability, ReplicatedEventBinding> BuildFactory(Type valueType)
        {
            var bindingType = typeof(ReplicatedEventBinding<>).MakeGenericType(valueType);
            var subjectType = typeof(Subject<>).MakeGenericType(valueType);
            var ctor = bindingType.GetConstructor(new[] { subjectType, typeof(AuthorityMode), typeof(Reliability) })
                ?? throw new InvalidOperationException($"No (Subject<T>, AuthorityMode, Reliability) ctor on {bindingType}.");
            return (subject, authority, reliability) =>
            {
                try
                {
                    return (ReplicatedEventBinding)ctor.Invoke(new[] { subject, (object)authority, (object)reliability });
                }
                catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException or MissingMethodException or TypeLoadException)
                {
                    Debug.LogError(
                        $"[ReplicatedEventBindingFactory] Failed to construct {bindingType.FullName}. " +
                        $"Most likely IL2CPP stripped the closed generic — add the payload type " +
                        $"to Assets/link.xml with preserve=\"all\". Inner: {ex.InnerException}");
                    throw;
                }
            };
        }
    }
}
