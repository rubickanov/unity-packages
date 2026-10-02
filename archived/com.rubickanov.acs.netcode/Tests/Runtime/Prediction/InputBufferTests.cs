using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class InputBufferTests
    {
        // Covers the step-6.1 contract: tick-keyed storage with strict matching,
        // wrap-around rejection, and hold-last fallback that skips gaps. These
        // are pure-struct tests — no NetworkManager needed.

        private struct TestInput : IInputCommand
        {
            public int Value;
        }

        [Test]
        public void Store_ThenTryGet_ReturnsSameInputForSameTick()
        {
            var buffer = InputBuffer<TestInput>.Create();
            var input = new TestInput { Value = 42 };

            buffer.Store(10, in input);

            Assert.IsTrue(buffer.TryGet(10, out var retrieved));
            Assert.AreEqual(42, retrieved.Value);
        }

        [Test]
        public void TryGet_UnstoredTick_ReturnsFalse()
        {
            var buffer = InputBuffer<TestInput>.Create();

            Assert.IsFalse(buffer.TryGet(5, out _));
        }

        [Test]
        public void TryGet_AfterWrapAround_RejectsOldTick()
        {
            // Storing tick 10 occupies slot 10 % 64 = 10. Storing tick 74 overwrites
            // the same slot. TryGet(10) must return false — strict tick match prevents
            // the wrap-around from reporting a stale positive.
            var buffer = InputBuffer<TestInput>.Create();
            var a = new TestInput { Value = 1 };
            var b = new TestInput { Value = 2 };

            buffer.Store(10, in a);
            buffer.Store(10 + InputBuffer<TestInput>.Capacity, in b);

            Assert.IsFalse(buffer.TryGet(10, out _));
            Assert.IsTrue(buffer.TryGet(10 + InputBuffer<TestInput>.Capacity, out var retrieved));
            Assert.AreEqual(2, retrieved.Value);
        }

        [Test]
        public void GetOrHoldLast_SkipsGap_ReturnsPriorTick()
        {
            // Server hold-last semantics: asked for tick 7, slot 7 empty, slot 5
            // populated → 5 is returned. Gap 6 is intentionally left empty.
            var buffer = InputBuffer<TestInput>.Create();
            var input5 = new TestInput { Value = 5 };
            buffer.Store(5, in input5);

            Assert.IsTrue(buffer.GetOrHoldLast(7, out var retrieved));
            Assert.AreEqual(5, retrieved.Value);
        }

        [Test]
        public void GetOrHoldLast_NothingStored_ReturnsFalse()
        {
            var buffer = InputBuffer<TestInput>.Create();
            Assert.IsFalse(buffer.GetOrHoldLast(3, out _));
        }

        [Test]
        public void GetOrHoldLast_TickBelowNewest_ReturnsExactMatchNotNewer()
        {
            // Guard against the wrap-around clamp: if newest is 70 and we ask for
            // tick 5 (strictly less), GetOrHoldLast must find slot 5 itself (same
            // ring slot modulo 64) WITHOUT falsely matching slot 70.
            var buffer = InputBuffer<TestInput>.Create();
            var early = new TestInput { Value = 1 };
            var late = new TestInput { Value = 99 };
            buffer.Store(5, in early);
            buffer.Store(70, in late);

            // Slot 5 has been overwritten by tick 69 % 64 = 5? No, 70 % 64 = 6;
            // slot 5 still holds the early input. Asking for tick 5 walks backwards
            // from 5 and finds itself.
            Assert.IsTrue(buffer.GetOrHoldLast(5, out var retrieved));
            Assert.AreEqual(1, retrieved.Value);
        }
    }
}
