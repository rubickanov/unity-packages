using System;
using NUnit.Framework;
using Rubickanov.Utils;

namespace Rubickanov.Utils.Tests
{
    public class CircularBufferTests
    {
        [Test]
        public void Add_ThenGet_ReturnsSameValue()
        {
            var buffer = new CircularBuffer<int>(4u);

            buffer.Add(42, 0u);

            Assert.AreEqual(42, buffer.Get(0u));
        }

        [Test]
        public void Get_WrapAround_ReturnsCorrectValue()
        {
            var buffer = new CircularBuffer<int>(4u);

            buffer.Add(99, 2u);
            int value = buffer.Get(2u + 4u);

            Assert.AreEqual(99, value);
        }

        [Test]
        public void Add_SameSlot_OverwritesPreviousValue()
        {
            var buffer = new CircularBuffer<int>(4u);

            buffer.Add(1, 0u);
            buffer.Add(2, 0u);

            Assert.AreEqual(2, buffer.Get(0u));
        }

        [Test]
        public void Add_WrapAround_OverwritesSlot()
        {
            var buffer = new CircularBuffer<int>(4u);

            buffer.Add(10, 1u);
            buffer.Add(20, 5u); // 5 % 4 == 1

            Assert.AreEqual(20, buffer.Get(1u));
        }

        [Test]
        public void Clear_ResetsToDefault()
        {
            var buffer = new CircularBuffer<int>(4u);

            buffer.Add(42, 0u);
            buffer.Add(43, 1u);
            buffer.Clear();

            Assert.AreEqual(0, buffer.Get(0u));
            Assert.AreEqual(0, buffer.Get(1u));
        }

        [Test]
        public void Capacity1_WorksCorrectly()
        {
            var buffer = new CircularBuffer<int>(1u);

            buffer.Add(5, 0u);
            Assert.AreEqual(5, buffer.Get(0u));

            buffer.Add(10, 1u); // 1 % 1 == 0
            Assert.AreEqual(10, buffer.Get(0u));
        }

        [Test]
        public void ReferenceType_ReturnsNullAfterClear()
        {
            var buffer = new CircularBuffer<string>(2u);

            buffer.Add("hello", 0u);
            buffer.Clear();

            Assert.IsNull(buffer.Get(0u));
        }

        [Test]
        public void Constructor_ZeroCapacity_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CircularBuffer<int>(0u));
        }

        [Test]
        public void Capacity_ReturnsConstructorValue()
        {
            var buffer = new CircularBuffer<int>(128u);

            Assert.AreEqual(128u, buffer.Capacity);
        }

        [Test]
        public void Get_UintUnderflow_WrapsToValidSlot()
        {
            // C2 regression: look-back across uint underflow must land in a valid slot.
            var buffer = new CircularBuffer<int>(16u);
            uint tick = 3u;

            buffer.Add(777, (tick - 10u) % 16u);
            int value = buffer.Get(tick - 10u);

            Assert.AreEqual(777, value);
        }
    }
}
