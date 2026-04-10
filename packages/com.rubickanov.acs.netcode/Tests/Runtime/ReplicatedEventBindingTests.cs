using System;
using System.Collections.Generic;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class ReplicatedEventBindingTests
    {
        // ---- Helpers ------------------------------------------------------------

        private static ReplicatedEventBinding<T> CreateBinding<T>(
            Subject<T> subject,
            AuthorityMode authority = AuthorityMode.Server,
            Reliability reliability = Reliability.Reliable)
            where T : unmanaged
        {
            return (ReplicatedEventBinding<T>)
                ReplicatedEventBindingFactory.Create(subject, typeof(T), authority, reliability);
        }

        /// <summary>
        /// Mock broadcaster that captures the T-payload bytes (stripping the
        /// networkObjectId + eventIndex header that OnLocalEvent prepends).
        /// </summary>
        private sealed class CapturingBroadcaster : IEventBroadcaster
        {
            public readonly List<(byte index, byte[] payload)> Captured = new();

            public unsafe void SendEvent(ulong networkObjectId, byte eventIndex,
                FastBufferWriter writer, AuthorityMode authority, Reliability reliability,
                bool isOwnerSubmit)
            {
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    reader.ReadValueSafe(out ulong _);
                    reader.ReadValueSafe(out byte _);
                    int remaining = reader.Length - reader.Position;
                    var payload = new byte[remaining];
                    fixed (byte* ptr = payload)
                        reader.ReadBytesSafe(ptr, remaining);
                    Captured.Add((eventIndex, payload));
                }
                finally
                {
                    reader.Dispose();
                }
            }
        }

        /// <summary>
        /// Subscribes <paramref name="binding"/> as authority with a capturing broadcaster
        /// and returns the list that each invocation appends to. Caller owns the bag.
        /// </summary>
        private static List<(byte index, byte[] bytes)> SubscribeCapturing<T>(
            ReplicatedEventBinding<T> binding, byte eventIndex, ref DisposableBag bag)
            where T : unmanaged
        {
            var broadcaster = new CapturingBroadcaster();
            binding.SubscribeAsAuthority(ref bag, eventIndex, broadcaster, networkObjectId: 0, isOwnerSubmit: false);
            return broadcaster.Captured;
        }

        /// <summary>
        /// Writes <paramref name="value"/> into a fresh FastBufferWriter, then calls
        /// ApplyFromNetwork on the binding with the matching reader. Mimics the peer-side
        /// RPC path synchronously.
        /// </summary>
        private static unsafe void ApplyAsNetwork<T>(ReplicatedEventBinding<T> binding, T value)
            where T : unmanaged
        {
            var writer = new FastBufferWriter(sizeof(T), Allocator.Temp);
            try
            {
                writer.WriteBytesSafe((byte*)&value, sizeof(T));
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try { binding.ApplyFromNetwork(reader); }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        // ---- Authority path: OnLocalEvent -> broadcaster -------------------------

        [Test]
        public void SubscribeAsAuthority_LocalOnNext_InvokesBroadcasterOnceWithDeclaredEventIndex()
        {
            var subject = new Subject<int>();
            var binding = CreateBinding(subject);
            var bag = new DisposableBag();
            try
            {
                var captured = SubscribeCapturing(binding, eventIndex: 7, ref bag);

                subject.OnNext(42);

                Assert.AreEqual(1, captured.Count, "broadcaster must fire exactly once per OnNext");
                Assert.AreEqual((byte)7, captured[0].index,
                    "eventIndex stored at SubscribeAsAuthority must be forwarded to the broadcaster");
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public unsafe void SubscribeAsAuthority_LocalOnNext_BroadcasterBytesDecodeBackToOriginalValue()
        {
            // If sizeof(T) is miscounted, the byte pointer is wrong, or endianness is flipped,
            // the decoded value will not equal the original. 0x11223344 has distinct bytes in
            // every position so any off-by-one or wrong-position write is visible.
            var subject = new Subject<int>();
            var binding = CreateBinding(subject);
            var bag = new DisposableBag();
            try
            {
                var captured = SubscribeCapturing(binding, eventIndex: 0, ref bag);

                subject.OnNext(0x11223344);

                Assert.AreEqual(1, captured.Count);
                Assert.AreEqual(sizeof(int), captured[0].bytes.Length,
                    "broadcaster bytes must be exactly sizeof(T)");

                var reader = new FastBufferReader(captured[0].bytes, Allocator.Temp);
                try
                {
                    int decoded = 0;
                    reader.ReadBytesSafe((byte*)&decoded, sizeof(int));
                    Assert.AreEqual(0x11223344, decoded);
                }
                finally { reader.Dispose(); }
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Constructor_WithoutSubscribeAsAuthority_OnNextDoesNotInvokeAnyBroadcasterPath()
        {
            // Guard: the constructor must not eager-subscribe the internal OnLocalEvent handler.
            // If it did, Subject.OnNext would call `_broadcaster!.Invoke(...)` with broadcaster
            // still null -> NullReferenceException. The local counter also proves the subject
            // itself still works (sanity check), which in turn proves `binding` is alive and
            // observably connected to the subject — no synthetic IsNotNull needed.
            var subject = new Subject<int>();
            _ = CreateBinding(subject);
            int localCount = 0;
            using (subject.Subscribe(_ => localCount++))
            {
                Assert.DoesNotThrow(() => subject.OnNext(99),
                    "constructor must not eager-subscribe OnLocalEvent — broadcaster is still null");
                Assert.AreEqual(1, localCount, "plain subject subscriber must still see the value");
            }
        }

        // ---- Non-authority path: ApplyFromNetwork -> subject.OnNext (#12b) -------

        [Test]
        public void ApplyFromNetwork_FiresLocalSubjectSubscriber_RegressionTwelveB()
        {
            // Regression #12b — guards removal of dead _suppressNotification flag from
            // ReplicatedEventBinding<T> (batch 3.1, 2026-04-09). If anyone wraps
            // _subject.OnNext(value) in ApplyFromNetwork with a suppression flag,
            // peer-side observers would silently stop receiving network-delivered events.
            // This test fails in that scenario because `received` stays empty.
            var subject = new Subject<int>();
            var binding = CreateBinding(subject);
            var received = new List<int>();
            using (subject.Subscribe(v => received.Add(v)))
            {
                ApplyAsNetwork(binding, 123);

                Assert.AreEqual(1, received.Count,
                    "ApplyFromNetwork must call _subject.OnNext — no suppression flag allowed around it");
                Assert.AreEqual(123, received[0]);
            }
        }

        [Test]
        public void ApplyFromNetwork_TwoIndependentSubscribers_BothFireWithSameValue()
        {
            // Defends against partial-suppression hacks that notify only one subscription chain.
            var subject = new Subject<int>();
            var binding = CreateBinding(subject);
            int a = 0, b = 0;
            using (subject.Subscribe(v => a = v))
            using (subject.Subscribe(v => b = v))
            {
                ApplyAsNetwork(binding, 77);

                Assert.AreEqual(77, a);
                Assert.AreEqual(77, b);
            }
        }

        [Test]
        public void ApplyFromNetwork_CalledThreeTimes_SubscriberCapturesAllThreeValuesInOrder()
        {
            // Guards against "fire only first" / dedup regressions — ApplyFromNetwork must
            // propagate every invocation, preserving order.
            var subject = new Subject<int>();
            var binding = CreateBinding(subject);
            var received = new List<int>();
            using (subject.Subscribe(v => received.Add(v)))
            {
                ApplyAsNetwork(binding, 1);
                ApplyAsNetwork(binding, 2);
                ApplyAsNetwork(binding, 3);

                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
            }
        }

        // ---- Sender -> wire bytes -> receiver round-trip --------------------------

        [Test]
        public unsafe void RoundTrip_Int_CapturedBroadcasterBytesReplayedViaReceiverApplyFromNetwork_DeliversSameValue()
        {
            // Full production path: sender (authority) subject.OnNext -> OnLocalEvent ->
            // broadcaster bytes -> receiver binding reads those same bytes via ApplyFromNetwork
            // -> receiver's local subscriber sees the value.
            var senderSubject = new Subject<int>();
            var sender = CreateBinding(senderSubject);
            var bag = new DisposableBag();
            try
            {
                var captured = SubscribeCapturing(sender, eventIndex: 0, ref bag);

                senderSubject.OnNext(0x11223344);
                Assert.AreEqual(1, captured.Count, "precondition: sender must broadcast exactly once");

                var receiverSubject = new Subject<int>();
                var receiver = CreateBinding(receiverSubject);
                var received = new List<int>();
                using (receiverSubject.Subscribe(v => received.Add(v)))
                {
                    var reader = new FastBufferReader(captured[0].bytes, Allocator.Temp);
                    try { receiver.ApplyFromNetwork(reader); }
                    finally { reader.Dispose(); }
                }

                Assert.AreEqual(1, received.Count);
                Assert.AreEqual(0x11223344, received[0]);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public unsafe void RoundTrip_Vector3_PreservesAllComponents()
        {
            var senderSubject = new Subject<Vector3>();
            var sender = CreateBinding(senderSubject);
            var bag = new DisposableBag();
            try
            {
                var captured = SubscribeCapturing(sender, eventIndex: 0, ref bag);

                var input = new Vector3(1.5f, -2.5f, 3.5f);
                senderSubject.OnNext(input);
                Assert.AreEqual(1, captured.Count);
                Assert.AreEqual(sizeof(Vector3), captured[0].bytes.Length);

                var receiverSubject = new Subject<Vector3>();
                var receiver = CreateBinding(receiverSubject);
                Vector3 received = Vector3.one * -999f;
                using (receiverSubject.Subscribe(v => received = v))
                {
                    var reader = new FastBufferReader(captured[0].bytes, Allocator.Temp);
                    try { receiver.ApplyFromNetwork(reader); }
                    finally { reader.Dispose(); }
                }

                Assert.AreEqual(input.x, received.x, 0f);
                Assert.AreEqual(input.y, received.y, 0f);
                Assert.AreEqual(input.z, received.z, 0f);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public unsafe void RoundTrip_CustomUnmanagedStruct_PreservesAllFields()
        {
            var senderSubject = new Subject<PackedEvent>();
            var sender = CreateBinding(senderSubject);
            var bag = new DisposableBag();
            try
            {
                var captured = SubscribeCapturing(sender, eventIndex: 0, ref bag);

                var input = new PackedEvent { A = 0x11223344, B = -7.25f, C = 0xAB };
                senderSubject.OnNext(input);
                Assert.AreEqual(1, captured.Count);
                Assert.AreEqual(sizeof(PackedEvent), captured[0].bytes.Length);

                var receiverSubject = new Subject<PackedEvent>();
                var receiver = CreateBinding(receiverSubject);
                PackedEvent received = default;
                using (receiverSubject.Subscribe(v => received = v))
                {
                    var reader = new FastBufferReader(captured[0].bytes, Allocator.Temp);
                    try { receiver.ApplyFromNetwork(reader); }
                    finally { reader.Dispose(); }
                }

                Assert.AreEqual(input.A, received.A);
                Assert.AreEqual(input.B, received.B, 0f);
                Assert.AreEqual(input.C, received.C);
            }
            finally { bag.Dispose(); }
        }

        // ---- Constructor surface ------------------------------------------------

        [Test]
        public void Authority_PassedToConstructor_ExposedViaProperty()
        {
            // Guards against constructor-arg swap (authority <-> reliability).
            var subject = new Subject<int>();
            var binding = CreateBinding(subject, authority: AuthorityMode.Owner);

            Assert.AreEqual(AuthorityMode.Owner, binding.Authority);
        }

        [Test]
        public void Reliability_PassedToConstructor_ExposedViaProperty()
        {
            var subject = new Subject<int>();
            var binding = CreateBinding(subject, reliability: Reliability.Unreliable);

            Assert.AreEqual(Reliability.Unreliable, binding.Reliability);
        }

        // ---- Test fixtures ------------------------------------------------------

        private struct PackedEvent
        {
            public int A;
            public float B;
            public byte C;
        }
    }
}
