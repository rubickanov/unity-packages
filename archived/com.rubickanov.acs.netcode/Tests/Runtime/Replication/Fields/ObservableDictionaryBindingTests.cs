using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    /// <summary>
    /// Delta-replication round-trip for ObservableDictionaryBinding&lt;K,V&gt;. The
    /// authority-side binding observes mutations on its dictionary and produces wire
    /// bytes; the receiver-side binding consumes them and re-produces the same
    /// mutations on its own dictionary. Mirrors ObservableListBindingTests' structure
    /// to keep collection bindings symmetric.
    /// </summary>
    [TestFixture]
    public class ObservableDictionaryBindingTests
    {
        private static void RoundTrip<TKey, TValue>(
            ObservableDictionaryBinding<TKey, TValue> sender,
            ObservableDictionaryBinding<TKey, TValue> receiver,
            bool snapshot = false) where TValue : unmanaged
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

        private static (ObservableDictionary<string, int> dict, ObservableDictionaryBinding<string, int> binding, DisposableBag bag)
            MakeStringKeyAuthority()
        {
            var dict = new ObservableDictionary<string, int>();
            var binding = new ObservableDictionaryBinding<string, int>(
                dict, StringKeyCodec.Instance, new RawCodec<int>());
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);
            return (dict, binding, bag);
        }

        private static (ObservableDictionary<string, int> dict, ObservableDictionaryBinding<string, int> binding)
            MakeStringKeyReceiver()
        {
            var dict = new ObservableDictionary<string, int>();
            var binding = new ObservableDictionaryBinding<string, int>(
                dict, StringKeyCodec.Instance, new RawCodec<int>());
            return (dict, binding);
        }

        private static (ObservableDictionary<int, int> dict, ObservableDictionaryBinding<int, int> binding, DisposableBag bag)
            MakeIntKeyAuthority()
        {
            var dict = new ObservableDictionary<int, int>();
            var binding = new ObservableDictionaryBinding<int, int>(
                dict,
                new UnmanagedKeyCodec<int>(new RawCodec<int>()),
                new RawCodec<int>());
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);
            return (dict, binding, bag);
        }

        private static (ObservableDictionary<int, int> dict, ObservableDictionaryBinding<int, int> binding)
            MakeIntKeyReceiver()
        {
            var dict = new ObservableDictionary<int, int>();
            var binding = new ObservableDictionaryBinding<int, int>(
                dict,
                new UnmanagedKeyCodec<int>(new RawCodec<int>()),
                new RawCodec<int>());
            return (dict, binding);
        }

        [Test]
        public void IsDirty_AfterConstruction_IsFalse()
        {
            var (_, binding, bag) = MakeStringKeyAuthority();
            try
            {
                Assert.IsFalse(binding.IsDirty);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void IsDirty_AfterAdd_IsTrueUntilClearDirty()
        {
            var (dict, binding, bag) = MakeStringKeyAuthority();
            try
            {
                dict.Add("alpha", 1);

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
        public void RoundTrip_Add_ReceiverObservesSameEntries()
        {
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (receiverDict, receiver) = MakeStringKeyReceiver();
            try
            {
                senderDict.Add("alpha", 10);
                senderDict.Add("beta", 20);
                senderDict.Add("gamma", 30);

                RoundTrip(sender, receiver);

                Assert.AreEqual(3, receiverDict.Count);
                Assert.AreEqual(10, receiverDict["alpha"]);
                Assert.AreEqual(20, receiverDict["beta"]);
                Assert.AreEqual(30, receiverDict["gamma"]);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Replace_ReceiverSeesNewValueForKey()
        {
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (receiverDict, receiver) = MakeStringKeyReceiver();
            try
            {
                senderDict.Add("key", 1);
                senderDict["key"] = 99;

                RoundTrip(sender, receiver);

                Assert.AreEqual(99, receiverDict["key"]);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Remove_ReceiverDropsKey()
        {
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (receiverDict, receiver) = MakeStringKeyReceiver();
            try
            {
                senderDict.Add("a", 1);
                senderDict.Add("b", 2);
                senderDict.Remove("a");

                RoundTrip(sender, receiver);

                Assert.IsFalse(receiverDict.ContainsKey("a"));
                Assert.AreEqual(2, receiverDict["b"]);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_Clear_ReceiverBecomesEmpty()
        {
            // Locks the Clear opcode wire path: ObservableDictionary.Clear() fires
            // ObserveReset on the authority, which the binding converts into a single
            // Clear op — the receiver applies it via _dict.Clear().
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (receiverDict, receiver) = MakeStringKeyReceiver();
            try
            {
                senderDict.Add("a", 1);
                senderDict.Add("b", 2);
                RoundTrip(sender, receiver);

                senderDict.Clear();

                RoundTrip(sender, receiver);

                Assert.AreEqual(0, receiverDict.Count);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void RoundTrip_AddWithUnmanagedKey_ReceiverObservesEntry()
        {
            var (senderDict, sender, bag) = MakeIntKeyAuthority();
            var (receiverDict, receiver) = MakeIntKeyReceiver();
            try
            {
                senderDict.Add(7, 70);
                senderDict.Add(42, 420);

                RoundTrip(sender, receiver);

                Assert.AreEqual(2, receiverDict.Count);
                Assert.AreEqual(70, receiverDict[7]);
                Assert.AreEqual(420, receiverDict[42]);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void WriteTo_DrainsPendingOps_SubsequentWriteIsEmpty()
        {
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (receiverDict, receiver) = MakeStringKeyReceiver();
            try
            {
                senderDict.Add("k", 1);
                RoundTrip(sender, receiver);

                int sizeBeforeSecondTick = sender.Size;
                RoundTrip(sender, receiver);

                Assert.AreEqual(ObservableCollectionBinding.HeaderBytes, sizeBeforeSecondTick,
                    "After a drain and with no new ops, Size must be the empty-delta framing overhead.");
                Assert.AreEqual(1, receiverDict["k"]);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Snapshot_LateJoinReceiver_EndsUpWithFullAuthorityContents()
        {
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            try
            {
                senderDict.Add("a", 7);
                senderDict.Add("b", 8);
                senderDict.Add("c", 9);

                // Stale receiver with entries the authority doesn't know about — the
                // snapshot's leading Clear must wipe these before AddKey refills.
                var receiverDict = new ObservableDictionary<string, int>
                {
                    { "stale1", 100 },
                    { "stale2", 200 },
                };
                var receiver = new ObservableDictionaryBinding<string, int>(
                    receiverDict, StringKeyCodec.Instance, new RawCodec<int>());

                RoundTrip(sender, receiver, snapshot: true);

                Assert.AreEqual(3, receiverDict.Count);
                Assert.AreEqual(7, receiverDict["a"]);
                Assert.AreEqual(8, receiverDict["b"]);
                Assert.AreEqual(9, receiverDict["c"]);
                Assert.IsFalse(receiverDict.ContainsKey("stale1"));
                Assert.IsFalse(receiverDict.ContainsKey("stale2"));
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void Skip_AdvancesReaderPastEntirePayload()
        {
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (_, receiver) = MakeStringKeyReceiver();
            try
            {
                senderDict.Add("alpha", 1);
                senderDict.Add("beta", 2);
                senderDict.Add("gamma", 3);

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
        public void ReceiverSubscription_IsNotRequired_ForApplyToFireUserHandlers()
        {
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (receiverDict, receiver) = MakeStringKeyReceiver();
            var addsObservedByUser = new List<KeyValuePair<string, int>>();
            using var sub = receiverDict.ObserveAdd()
                .Subscribe(e => addsObservedByUser.Add(new KeyValuePair<string, int>(e.Value.Key, e.Value.Value)));
            try
            {
                senderDict.Add("x", 5);
                senderDict.Add("y", 6);

                RoundTrip(sender, receiver);

                Assert.AreEqual(2, addsObservedByUser.Count);
                Assert.IsTrue(addsObservedByUser.Any(kv => kv.Key == "x" && kv.Value == 5));
                Assert.IsTrue(addsObservedByUser.Any(kv => kv.Key == "y" && kv.Value == 6));
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void StringKeyCodec_RoundTripsEmptyAndUnicodeStrings()
        {
            // Locks multi-byte UTF-8 encoding in StringKeyCodec. Cyrillic + emoji keys
            // are the realistic stress test — they produce 2–4 byte sequences that must
            // survive the ushort byteLen + bytes wire format intact.
            var (senderDict, sender, bag) = MakeStringKeyAuthority();
            var (receiverDict, receiver) = MakeStringKeyReceiver();
            try
            {
                senderDict.Add("", 0);
                senderDict.Add("ключ", 1);
                senderDict.Add("🔑emoji", 2);

                RoundTrip(sender, receiver);

                Assert.AreEqual(0, receiverDict[""]);
                Assert.AreEqual(1, receiverDict["ключ"]);
                Assert.AreEqual(2, receiverDict["🔑emoji"]);
            }
            finally { bag.Dispose(); }
        }
    }
}
