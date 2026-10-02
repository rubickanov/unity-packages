using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class ConsoleLogTests
    {
        [SetUp]
        public void SetUp() => ConsoleLog.Clear();

        [TearDown]
        public void TearDown() => ConsoleLog.Clear();

        [Test]
        public void Log_PastCapacity_KeepsTheNewestInOrder()
        {
            var first = ConsoleLog.FirstNumber;

            for (int i = 0; i < ConsoleLog.Capacity + 5; i++) ConsoleLog.Log($"line {i}");

            Assert.AreEqual(ConsoleLog.Capacity, ConsoleLog.Entries.Count);
            Assert.AreEqual("line 5", ConsoleLog.Entries[0].Message);
            Assert.AreEqual($"line {ConsoleLog.Capacity + 4}", ConsoleLog.Entries[ConsoleLog.Capacity - 1].Message);
            Assert.AreEqual(first + 5, ConsoleLog.FirstNumber);
        }

        [Test]
        public void Entries_AfterWrapping_EnumerateLikeTheIndexer()
        {
            for (int i = 0; i < ConsoleLog.Capacity + 3; i++) ConsoleLog.Log($"line {i}");

            var enumerated = new List<string>();
            foreach (var entry in ConsoleLog.Entries) enumerated.Add(entry.Message);

            Assert.AreEqual(ConsoleLog.Capacity, enumerated.Count);
            for (int i = 0; i < enumerated.Count; i++)
                Assert.AreEqual(ConsoleLog.Entries[i].Message, enumerated[i]);
        }

        [Test]
        public void Clear_AfterWrapping_StartsEmptyAndKeepsNumbering()
        {
            for (int i = 0; i < ConsoleLog.Capacity + 3; i++) ConsoleLog.Log($"line {i}");
            var next = ConsoleLog.FirstNumber + ConsoleLog.Entries.Count;

            ConsoleLog.Clear();
            ConsoleLog.Log("after");

            Assert.AreEqual(1, ConsoleLog.Entries.Count);
            Assert.AreEqual("after", ConsoleLog.Entries[0].Message);
            Assert.AreEqual(next, ConsoleLog.FirstNumber);
        }

        [Test]
        public void Entries_IndexPastTheEnd_Throws()
        {
            ConsoleLog.Log("only");

            Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = ConsoleLog.Entries[1]);
        }
    }
}
