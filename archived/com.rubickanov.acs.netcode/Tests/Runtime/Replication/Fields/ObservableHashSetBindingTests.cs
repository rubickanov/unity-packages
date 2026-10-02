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
    /// Delta-replication round-trip for <see cref="ObservableHashSetBinding{T}"/>.
    /// Authority-side binding observes mutations on its set and produces wire bytes;
    /// receiver-side binding consumes those bytes and re-produces the same mutations
    /// on its own set. Mirrors <see cref="ObservableListBindingTests"/>; HashSet uses
    /// value-based ops (AddValue / RemoveValue / Clear) instead of index-based ops.
    /// </summary>
    [TestFixture]
    public class ObservableHashSetBindingTests
    {
        private static void RoundTrip<T>(
            ObservableHashSetBinding<T> sender,
            ObservableHashSetBinding<T> receiver,
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

        private static (ObservableHashSet<int> set, ObservableHashSetBinding<int> binding, DisposableBag bag)
            MakeAuthority()
        {
            var set = new ObservableHashSet<int>();
            var binding = new ObservableHashSetBinding<int>(set, new RawCodec<int>());
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);
            return (set, binding, bag);
        }

        private static (ObservableHashSet<int> set, ObservableHashSetBinding<int> binding) MakeReceiver()
        {
            var set = new ObservableHashSet<int>();
            var binding = new ObservableHashSetBinding<int>(set, new RawCodec<int>());
            return (set, binding);
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
        public void IsDirty_AfterAdd_IsTrueUntilClearDirty()
        {
            var (set, binding, bag) = MakeAuthority();
            try
            {
                set.Add(42);

                Assert.IsTrue(binding.IsDirty);
                var writer = new FastBufferWriter(128, Allocator.Temp);
                try { binding.WriteTo(writer); }
                finally { writer.Dispose(); }
                binding.ClearDirty();
                Assert.IsFalse(binding.IsDirty);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Add_ReceiverSetContainsSameValues()
        {
            var (senderSet, sender, bag) = MakeAuthority();
            var (receiverSet, receiver) = MakeReceiver();
            try
            {
                senderSet.Add(10);
                senderSet.Add(20);
                senderSet.Add(30);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEquivalent(new[] { 10, 20, 30 }, receiverSet);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Remove_ReceiverSetRemovesValue()
        {
            var (senderSet, sender, bag) = MakeAuthority();
            var (receiverSet, receiver) = MakeReceiver();
            try
            {
                senderSet.Add(1);
                senderSet.Add(2);
                senderSet.Add(3);
                senderSet.Remove(2);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEquivalent(new[] { 1, 3 }, receiverSet);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Clear_ReceiverSetBecomesEmpty()
        {
            var (senderSet, sender, bag) = MakeAuthority();
            var (receiverSet, receiver) = MakeReceiver();
            try
            {
                senderSet.Add(1);
                senderSet.Add(2);
                RoundTrip(sender, receiver);

                senderSet.Clear();

                RoundTrip(sender, receiver);

                Assert.AreEqual(0, receiverSet.Count);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void WriteTo_DrainsPendingOps_SecondTickPayloadIsHeaderOnly()
        {
            var (senderSet, sender, bag) = MakeAuthority();
            var (receiverSet, receiver) = MakeReceiver();
            try
            {
                senderSet.Add(1);
                RoundTrip(sender, receiver);

                int sizeBeforeSecondTick = sender.Size;
                RoundTrip(sender, receiver);

                Assert.AreEqual(ObservableCollectionBinding.HeaderBytes, sizeBeforeSecondTick,
                    "After a drain and with no new ops, Size must be the empty-delta framing overhead.");
                CollectionAssert.AreEquivalent(new[] { 1 }, receiverSet);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Snapshot_LateJoinReceiver_StaleContentsWipedAndRefilled()
        {
            var (senderSet, sender, bag) = MakeAuthority();
            try
            {
                senderSet.Add(7);
                senderSet.Add(8);
                senderSet.Add(9);

                // Stale receiver with different contents — snapshot path must Clear
                // then refill.
                var receiverSet = new ObservableHashSet<int> { 100, 200, 300 };
                var receiver = new ObservableHashSetBinding<int>(receiverSet, new RawCodec<int>());

                RoundTrip(sender, receiver, snapshot: true);

                CollectionAssert.AreEquivalent(new[] { 7, 8, 9 }, receiverSet);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Skip_AdvancesReaderPastEntirePayload()
        {
            var (senderSet, sender, bag) = MakeAuthority();
            var (_, receiver) = MakeReceiver();
            try
            {
                senderSet.Add(1);
                senderSet.Add(2);
                senderSet.Add(3);

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
        public void ReceiverObserveAdd_FiresOnReadFrom_UserHandlersSeeTheMutation()
        {
            var (senderSet, sender, bag) = MakeAuthority();
            var (receiverSet, receiver) = MakeReceiver();
            var addsObservedByUser = new List<int>();
            using var sub = receiverSet.ObserveAdd().Subscribe(e => addsObservedByUser.Add(e.Value));
            try
            {
                senderSet.Add(5);
                senderSet.Add(6);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEquivalent(new[] { 5, 6 }, addsObservedByUser);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void ReceiverRemove_OnMissingValue_LogsWarningAndContinues()
        {
            // A mid-stream reorder or a pure-synthetic RemoveValue delta targeting a
            // value the receiver doesn't hold must not throw; defensive-warn behaviour
            // matches ObservableListBinding.RemoveAt out-of-range.
            //
            // We build the wire payload by hand (length-prefix + opCount + one
            // RemoveValue op for a value the receiver does not contain) so the
            // receiver's ReadFrom exercises the missing-value branch directly.
            var (receiverSet, receiver) = MakeReceiver();
            var writer = new FastBufferWriter(64, Allocator.Temp);
            try
            {
                // Wire framing: ushort lengthBytes + ushort opCount + ops.
                // Content = opCount(2) + opcode(1) + int value(4) = 7 bytes.
                writer.WriteValueSafe((ushort)7);     // length prefix (= content bytes)
                writer.WriteValueSafe((ushort)1);     // opCount
                writer.WriteValueSafe((byte)9);       // CollectionOpCode.RemoveValue
                writer.WriteValueSafe(42);            // value not present on receiver

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "ObservableHashSetBinding.*RemoveValue for missing value"));

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    receiver.ReadFrom(reader);
                }
                finally { reader.Dispose(); }

                Assert.AreEqual(0, receiverSet.Count,
                    "Receiver must remain empty — the op was dropped, not applied.");
            }
            finally { writer.Dispose(); }
        }
    }
}
