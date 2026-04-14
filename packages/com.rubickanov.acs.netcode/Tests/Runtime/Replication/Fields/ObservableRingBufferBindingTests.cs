using System.Collections.Generic;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    /// <summary>
    /// Delta-replication round-trip for <see cref="ObservableRingBufferBinding{T}"/>.
    /// Only AddLast (+ Clear) transits the wire; receiver-side eviction is derived
    /// from its own capacity being configured identically to the authority's.
    /// </summary>
    [TestFixture]
    public class ObservableRingBufferBindingTests
    {
        private static void RoundTrip<T>(
            ObservableRingBufferBinding<T> sender,
            ObservableRingBufferBinding<T> receiver,
            bool snapshot = false) where T : unmanaged
        {
            var writer = new FastBufferWriter(1024, Allocator.Temp);
            try
            {
                if (snapshot) sender.WriteSnapshotTo(writer);
                else sender.WriteTo(writer);

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    receiver.ReadFrom(reader);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        private static (ObservableFixedSizeRingBuffer<int> buffer, ObservableRingBufferBinding<int> binding, DisposableBag bag)
            MakeAuthority(int capacity = 3)
        {
            var buffer = new ObservableFixedSizeRingBuffer<int>(capacity);
            var binding = new ObservableRingBufferBinding<int>(buffer, new RawCodec<int>());
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);
            return (buffer, binding, bag);
        }

        private static (ObservableFixedSizeRingBuffer<int> buffer, ObservableRingBufferBinding<int> binding)
            MakeReceiver(int capacity = 3)
        {
            var buffer = new ObservableFixedSizeRingBuffer<int>(capacity);
            var binding = new ObservableRingBufferBinding<int>(buffer, new RawCodec<int>());
            return (buffer, binding);
        }

        private static int[] ToArray(ObservableFixedSizeRingBuffer<int> buffer)
        {
            // Avoid depending on Cysharp's ToArray extension placement.
            var list = new List<int>(buffer.Count);
            foreach (var v in buffer) list.Add(v);
            return list.ToArray();
        }

        [Test]
        public void IsDirty_AfterConstruction_IsFalse()
        {
            var (_, binding, bag) = MakeAuthority();
            try
            {
                Assert.IsFalse(binding.IsDirty);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_AddLast_ReceiverBufferContainsSameValues()
        {
            var (senderBuffer, sender, bag) = MakeAuthority(capacity: 4);
            var (receiverBuffer, receiver) = MakeReceiver(capacity: 4);
            try
            {
                senderBuffer.AddLast(10);
                senderBuffer.AddLast(20);
                senderBuffer.AddLast(30);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEqual(new[] { 10, 20, 30 }, ToArray(receiverBuffer));
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void AddLast_BeyondCapacity_ReceiverAutoEvictsOldest_NoExplicitRemoveOnWire()
        {
            // Capacity 3 on both peers. Pushing 4 values on the authority must
            // produce a buffer of [2, 3, 4] on the receiver — derived locally via
            // its own AddLast auto-eviction. The authority does NOT transmit a
            // remove op for the evicted first item.
            var (senderBuffer, sender, bag) = MakeAuthority(capacity: 3);
            var (receiverBuffer, receiver) = MakeReceiver(capacity: 3);
            var receiverRemovals = new List<int>();
            using var sub = receiverBuffer.ObserveRemove().Subscribe(e => receiverRemovals.Add(e.Value));
            try
            {
                senderBuffer.AddLast(1);
                senderBuffer.AddLast(2);
                senderBuffer.AddLast(3);
                senderBuffer.AddLast(4);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEqual(new[] { 2, 3, 4 }, ToArray(receiverBuffer));
                Assert.AreEqual(1, receiverRemovals.Count,
                    "Receiver ring buffer must fire ObserveRemove for the evicted oldest element as a local side-effect of AddLast.");
                Assert.AreEqual(1, receiverRemovals[0]);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Clear_ReceiverBufferBecomesEmpty()
        {
            var (senderBuffer, sender, bag) = MakeAuthority();
            var (receiverBuffer, receiver) = MakeReceiver();
            try
            {
                senderBuffer.AddLast(1);
                senderBuffer.AddLast(2);
                RoundTrip(sender, receiver);

                senderBuffer.Clear();

                RoundTrip(sender, receiver);

                Assert.AreEqual(0, receiverBuffer.Count);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void WriteTo_DrainsPendingOps_SecondTickPayloadIsHeaderOnly()
        {
            var (senderBuffer, sender, bag) = MakeAuthority();
            var (receiverBuffer, receiver) = MakeReceiver();
            try
            {
                senderBuffer.AddLast(1);
                RoundTrip(sender, receiver);

                int sizeBeforeSecondTick = sender.Size;
                RoundTrip(sender, receiver);

                Assert.AreEqual(ObservableCollectionBinding.HeaderBytes, sizeBeforeSecondTick,
                    "After a drain and with no new ops, Size must be the empty-delta framing overhead.");
                CollectionAssert.AreEqual(new[] { 1 }, ToArray(receiverBuffer));
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Snapshot_LateJoinReceiver_StaleContentsWipedAndRefilled()
        {
            var (senderBuffer, sender, bag) = MakeAuthority(capacity: 3);
            try
            {
                senderBuffer.AddLast(7);
                senderBuffer.AddLast(8);
                senderBuffer.AddLast(9);

                var receiverBuffer = new ObservableFixedSizeRingBuffer<int>(capacity: 3);
                receiverBuffer.AddLast(100);
                receiverBuffer.AddLast(200);
                var receiver = new ObservableRingBufferBinding<int>(receiverBuffer, new RawCodec<int>());

                RoundTrip(sender, receiver, snapshot: true);

                CollectionAssert.AreEqual(new[] { 7, 8, 9 }, ToArray(receiverBuffer));
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Skip_AdvancesReaderPastEntirePayload()
        {
            var (senderBuffer, sender, bag) = MakeAuthority();
            var (_, receiver) = MakeReceiver();
            try
            {
                senderBuffer.AddLast(1);
                senderBuffer.AddLast(2);
                senderBuffer.AddLast(3);

                var writer = new FastBufferWriter(256, Allocator.Temp);
                try
                {
                    sender.WriteTo(writer);
                    int payloadEnd = writer.Position;
                    writer.WriteValueSafe(0xDEADBEEF);

                    var reader = new FastBufferReader(writer, Allocator.Temp);
                    try
                    {
                        receiver.Skip(reader);
                        Assert.AreEqual(payloadEnd, reader.Position,
                            "Skip must leave the reader positioned immediately after the field payload.");
                        reader.ReadValueSafe(out uint sentinel);
                        Assert.AreEqual(0xDEADBEEFu, sentinel);
                    }
                    finally { reader.Dispose(); }
                }
                finally { writer.Dispose(); }
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Snapshot_MismatchedCapacity_AuthorityLargerThanReceiver_LogsError()
        {
            // Authority capacity 5, receiver capacity 2. Snapshot replay (Clear + 5
            // AddLast) must cause the receiver to auto-evict as it replays, ending
            // up with only its capacity's worth of the most recent values. The
            // binding logs a targeted error so the wiring bug is surfaced.
            var senderBuffer = new ObservableFixedSizeRingBuffer<int>(capacity: 5);
            var sender = new ObservableRingBufferBinding<int>(senderBuffer, new RawCodec<int>());
            var bag = new DisposableBag();
            sender.SubscribeAsAuthority(ref bag);
            try
            {
                for (int i = 1; i <= 5; i++) senderBuffer.AddLast(i);

                var receiverBuffer = new ObservableFixedSizeRingBuffer<int>(capacity: 2);
                var receiver = new ObservableRingBufferBinding<int>(receiverBuffer, new RawCodec<int>());

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                    "ObservableRingBufferBinding.*Snapshot produced receiver-side eviction.*Capacity mismatch"));

                RoundTrip(sender, receiver, snapshot: true);

                // Receiver keeps the 2 most recent values — AddLast-only evict semantics.
                CollectionAssert.AreEqual(new[] { 4, 5 }, ToArray(receiverBuffer));
            }
            finally { bag.Dispose(); }
        }
    }
}
