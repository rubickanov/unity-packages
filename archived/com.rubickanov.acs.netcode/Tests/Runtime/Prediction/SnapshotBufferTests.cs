using System;
using NUnit.Framework;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class SnapshotBufferTests
    {
        // Covers the step-7 snapshot contract: tick-keyed byte-span slots with
        // strict match and wrap-around rejection. The buffer stores raw bytes
        // so tests exercise the ring directly — serialization is the
        // ReplicatedFieldBinding path, which has its own round-trip coverage.

        [Test]
        public void BeginWrite_ThenTryGet_ReturnsSameSlotBytesForSameTick()
        {
            var buffer = new SnapshotBuffer(8);

            Span<byte> writeSlot = buffer.BeginWrite(10);
            for (int i = 0; i < writeSlot.Length; i++)
                writeSlot[i] = (byte)(i + 1);

            Assert.IsTrue(buffer.TryGet(10, out var readSlot));
            Assert.AreEqual(writeSlot.Length, readSlot.Length);
            for (int i = 0; i < readSlot.Length; i++)
                Assert.AreEqual((byte)(i + 1), readSlot[i]);
        }

        [Test]
        public void TryGet_OnEmptyBuffer_ReturnsFalse()
        {
            var buffer = new SnapshotBuffer(4);

            Assert.IsFalse(buffer.TryGet(0, out _));
        }

        [Test]
        public void TryGet_UnwrittenTick_ReturnsFalse()
        {
            var buffer = new SnapshotBuffer(4);
            buffer.BeginWrite(3);

            Assert.IsFalse(buffer.TryGet(7, out _));
        }

        [Test]
        public void TryGet_AfterWrapAround_RejectsOldTick()
        {
            // Slot 10 % 64 = 10 is first written at tick 10, then overwritten
            // at tick 74. The strict tick match must reject the stale tick 10.
            var buffer = new SnapshotBuffer(2);

            Span<byte> a = buffer.BeginWrite(10);
            a[0] = 0xAA;
            a[1] = 0xBB;

            Span<byte> b = buffer.BeginWrite(10 + SnapshotBuffer.Capacity);
            b[0] = 0xCC;
            b[1] = 0xDD;

            Assert.IsFalse(buffer.TryGet(10, out _));
            Assert.IsTrue(buffer.TryGet(10 + SnapshotBuffer.Capacity, out var retrieved));
            Assert.AreEqual(0xCC, retrieved[0]);
            Assert.AreEqual(0xDD, retrieved[1]);
        }

        [Test]
        public void OldestTrackedTick_AfterWrite_ReflectsNewestMinusCapacity()
        {
            var buffer = new SnapshotBuffer(2);
            Assert.IsFalse(buffer.HasAny);

            buffer.BeginWrite(100);
            Assert.IsTrue(buffer.HasAny);
            Assert.AreEqual(100, buffer.NewestTick);
            Assert.AreEqual(100 - (SnapshotBuffer.Capacity - 1), buffer.OldestTrackedTick);
        }

        [Test]
        public void BeginWrite_SameTickTwice_OverwritesPreviousBytes()
        {
            var buffer = new SnapshotBuffer(2);

            Span<byte> first = buffer.BeginWrite(5);
            first[0] = 0x11;
            first[1] = 0x22;

            Span<byte> second = buffer.BeginWrite(5);
            second[0] = 0x33;
            second[1] = 0x44;

            Assert.IsTrue(buffer.TryGet(5, out var retrieved));
            Assert.AreEqual(0x33, retrieved[0]);
            Assert.AreEqual(0x44, retrieved[1]);
        }
    }
}
