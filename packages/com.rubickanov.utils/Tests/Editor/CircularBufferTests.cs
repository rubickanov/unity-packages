using NUnit.Framework;
using Rubickanov.Utils;

namespace Rubickanov.Utils.Tests
{
    public class CircularBufferTests
    {
        [Test]
        public void Add_ThenGet_ReturnsSameValue()
        {
            var buffer = new CircularBuffer<int>(4);

            buffer.Add(42, 0);

            Assert.AreEqual(42, buffer.Get(0));
        }

        [Test]
        public void Get_WrapAround_ReturnsCorrectValue()
        {
            var buffer = new CircularBuffer<int>(4);

            buffer.Add(99, 2);
            int value = buffer.Get(2 + 4);

            Assert.AreEqual(99, value);
        }

        [Test]
        public void Add_SameSlot_OverwritesPreviousValue()
        {
            var buffer = new CircularBuffer<int>(4);

            buffer.Add(1, 0);
            buffer.Add(2, 0);

            Assert.AreEqual(2, buffer.Get(0));
        }

        [Test]
        public void Add_WrapAround_OverwritesSlot()
        {
            var buffer = new CircularBuffer<int>(4);

            buffer.Add(10, 1);
            buffer.Add(20, 5); // 5 % 4 == 1

            Assert.AreEqual(20, buffer.Get(1));
        }

        [Test]
        public void Clear_ResetsToDefault()
        {
            var buffer = new CircularBuffer<int>(4);

            buffer.Add(42, 0);
            buffer.Add(43, 1);
            buffer.Clear();

            Assert.AreEqual(0, buffer.Get(0));
            Assert.AreEqual(0, buffer.Get(1));
        }

        [Test]
        public void Capacity1_WorksCorrectly()
        {
            var buffer = new CircularBuffer<int>(1);

            buffer.Add(5, 0);
            Assert.AreEqual(5, buffer.Get(0));

            buffer.Add(10, 1); // 1 % 1 == 0
            Assert.AreEqual(10, buffer.Get(0));
        }

        [Test]
        public void ReferenceType_ReturnsNullAfterClear()
        {
            var buffer = new CircularBuffer<string>(2);

            buffer.Add("hello", 0);
            buffer.Clear();

            Assert.IsNull(buffer.Get(0));
        }
    }
}
