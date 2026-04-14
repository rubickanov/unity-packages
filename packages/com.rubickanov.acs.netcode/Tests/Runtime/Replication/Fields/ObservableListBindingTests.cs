using System.Collections.Generic;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    /// <summary>
    /// Delta-replication round-trip for ObservableListBinding&lt;T&gt;. The authority-side
    /// binding observes mutations on its list and produces wire bytes; the receiver-side
    /// binding consumes those bytes and re-produces the same mutations on its own list.
    /// We lock that equivalence here without spinning up a NetworkManager — the binding
    /// is the only replication primitive with observable behaviour at this level.
    /// </summary>
    [TestFixture]
    public class ObservableListBindingTests
    {
        private static void RoundTrip<T>(
            ObservableListBinding<T> sender,
            ObservableListBinding<T> receiver,
            bool snapshot = false) where T : unmanaged
        {
            // Allow a generous buffer — tests send tiny payloads, the over-allocation
            // keeps FastBufferWriter out of the hot path of the assertion.
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

        private static (ObservableList<int> list, ObservableListBinding<int> binding, DisposableBag bag)
            MakeAuthority()
        {
            var list = new ObservableList<int>();
            var binding = new ObservableListBinding<int>(list, new RawCodec<int>());
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);
            return (list, binding, bag);
        }

        private static (ObservableList<int> list, ObservableListBinding<int> binding) MakeReceiver()
        {
            var list = new ObservableList<int>();
            var binding = new ObservableListBinding<int>(list, new RawCodec<int>());
            return (list, binding);
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
            var (list, binding, bag) = MakeAuthority();
            try
            {
                list.Add(42);

                Assert.IsTrue(binding.IsDirty);
                var writer = new FastBufferWriter(128, Allocator.Temp);
                try
                {
                    binding.WriteTo(writer);
                }
                finally { writer.Dispose(); }
                binding.ClearDirty();
                Assert.IsFalse(binding.IsDirty);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Add_ReceiverObservesSameValueAtSameIndex()
        {
            var (senderList, sender, bag) = MakeAuthority();
            var (receiverList, receiver) = MakeReceiver();
            try
            {
                senderList.Add(10);
                senderList.Add(20);
                senderList.Add(30);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEqual(new[] { 10, 20, 30 }, receiverList);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Insert_ReceiverMatchesElementOrder()
        {
            var (senderList, sender, bag) = MakeAuthority();
            var (receiverList, receiver) = MakeReceiver();
            try
            {
                senderList.Add(1);
                senderList.Add(3);
                senderList.Insert(1, 2);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, receiverList);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Replace_ReceiverSeesNewValueAtIndex()
        {
            var (senderList, sender, bag) = MakeAuthority();
            var (receiverList, receiver) = MakeReceiver();
            try
            {
                senderList.Add(1);
                senderList.Add(2);
                senderList.Add(3);
                senderList[1] = 99;

                RoundTrip(sender, receiver);

                CollectionAssert.AreEqual(new[] { 1, 99, 3 }, receiverList);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_RemoveAt_ReceiverShrinksInPlace()
        {
            var (senderList, sender, bag) = MakeAuthority();
            var (receiverList, receiver) = MakeReceiver();
            try
            {
                senderList.Add(1);
                senderList.Add(2);
                senderList.Add(3);
                senderList.RemoveAt(1);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEqual(new[] { 1, 3 }, receiverList);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Clear_ReceiverBecomesEmpty()
        {
            var (senderList, sender, bag) = MakeAuthority();
            var (receiverList, receiver) = MakeReceiver();
            try
            {
                senderList.Add(1);
                senderList.Add(2);
                RoundTrip(sender, receiver);

                // Clear fires a separate Reset event — verify it travels as a Clear op
                // and wipes the receiver collection.
                senderList.Clear();

                RoundTrip(sender, receiver);

                Assert.AreEqual(0, receiverList.Count);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void WriteTo_DrainsPendingOps_SubsequentWriteIsEmpty()
        {
            // Operational: deltas are drained on write. A second tick with no mutations
            // must produce an empty payload (header only) so bandwidth is not wasted.
            var (senderList, sender, bag) = MakeAuthority();
            var (receiverList, receiver) = MakeReceiver();
            try
            {
                senderList.Add(1);
                RoundTrip(sender, receiver);

                int sizeBeforeSecondTick = sender.Size;
                RoundTrip(sender, receiver);

                Assert.AreEqual(ObservableCollectionBinding.HeaderBytes, sizeBeforeSecondTick,
                    "After a drain and with no new ops, Size must be the empty-delta framing overhead.");
                CollectionAssert.AreEqual(new[] { 1 }, receiverList);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Snapshot_LateJoinReceiver_EndsUpWithFullAuthorityContents()
        {
            // Populate the authority's list via ordinary mutations, then imagine a new
            // peer joining — the binding builds a Clear + Insert* snapshot that, applied
            // to a fresh receiver, reproduces the authority state verbatim.
            var (senderList, sender, bag) = MakeAuthority();
            try
            {
                senderList.Add(7);
                senderList.Add(8);
                senderList.Add(9);

                // Simulate a late-joining receiver that has a non-empty stale collection.
                var receiverList = new ObservableList<int> { 100, 200, 300 };
                var receiver = new ObservableListBinding<int>(receiverList, new RawCodec<int>());

                RoundTrip(sender, receiver, snapshot: true);

                CollectionAssert.AreEqual(new[] { 7, 8, 9 }, receiverList);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Skip_AdvancesReaderPastEntirePayload()
        {
            // Skip is how AspectReplicator handles unknown entities on the wire — the
            // reader must end up exactly at the byte following the encoded field, no
            // matter how many ops were written. Regression guard: without the length
            // prefix, Skip would need to replay codec reads, coupling its behaviour to
            // the op schema.
            var (senderList, sender, bag) = MakeAuthority();
            var (_, receiver) = MakeReceiver();
            try
            {
                senderList.Add(1);
                senderList.Add(2);
                senderList.Add(3);

                var writer = new FastBufferWriter(256, Allocator.Temp);
                try
                {
                    // Frame: [payload] [sentinel 0xDEADBEEF]
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
        public void ReceiverSubscription_IsNotRequired_ForApplyToFireUserHandlers()
        {
            // User code subscribes to ObserveAdd on the receiver's aspect to update UI;
            // the binding itself should never own those subscriptions. Verify ReadFrom
            // does fire ObserveAdd on the receiver so downstream user code works.
            var (senderList, sender, bag) = MakeAuthority();
            var (receiverList, receiver) = MakeReceiver();
            var addsObservedByUser = new List<int>();
            using var sub = receiverList.ObserveAdd().Subscribe(e => addsObservedByUser.Add(e.Value));
            try
            {
                senderList.Add(5);
                senderList.Add(6);

                RoundTrip(sender, receiver);

                CollectionAssert.AreEqual(new[] { 5, 6 }, addsObservedByUser);
            }
            finally { bag.Dispose(); }
        }
    }
}
