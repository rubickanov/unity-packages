using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class UnityLogForwarderTests
    {
        [SetUp]
        public void SetUp()
        {
            UnityLogForwarder.Reset();
            ConsoleLog.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UnityLogForwarder.Reset();
            ConsoleLog.Clear();
        }

        [Test]
        public void Receive_OnMainThread_ReachesTheConsoleAtOnce()
        {
            UnityLogForwarder.Receive("spawned", "", LogType.Log);

            Assert.AreEqual(1, ConsoleLog.Entries.Count);
            Assert.AreEqual("spawned", ConsoleLog.Entries[0].Message);
            Assert.AreEqual(ConsoleLog.LogType.Info, ConsoleLog.Entries[0].Type);
        }

        [Test]
        public void Receive_Warning_IsAWarning()
        {
            UnityLogForwarder.Receive("low health", "at Foo()", LogType.Warning);

            Assert.AreEqual(ConsoleLog.LogType.Warning, ConsoleLog.Entries[0].Type);
            Assert.AreEqual("low health", ConsoleLog.Entries[0].Message);
        }

        [TestCase(LogType.Error)]
        [TestCase(LogType.Exception)]
        [TestCase(LogType.Assert)]
        public void Receive_ErrorKinds_AreErrorsWithTheStackTrace(LogType type)
        {
            UnityLogForwarder.Receive("broke", "at Foo()\nat Bar()\n", type);

            Assert.AreEqual(ConsoleLog.LogType.Error, ConsoleLog.Entries[0].Type);
            Assert.AreEqual("broke\nat Foo()\nat Bar()", ConsoleLog.Entries[0].Message);
        }

        [Test]
        public void Receive_ErrorWithoutStackTrace_IsJustTheMessage()
        {
            UnityLogForwarder.Receive("broke", "", LogType.Error);

            Assert.AreEqual("broke", ConsoleLog.Entries[0].Message);
        }

        [Test]
        public void Receive_Disabled_IsDropped()
        {
            UnityLogForwarder.Enabled = false;

            UnityLogForwarder.Receive("spawned", "", LogType.Log);

            Assert.AreEqual(0, ConsoleLog.Entries.Count);
        }

        [Test]
        public void Receive_FromAnotherThread_WaitsForTheDrain()
        {
            RunOnOtherThread(() => UnityLogForwarder.Receive("from worker", "", LogType.Log));

            Assert.AreEqual(0, ConsoleLog.Entries.Count);

            UnityLogForwarder.Drain();

            Assert.AreEqual(1, ConsoleLog.Entries.Count);
            Assert.AreEqual("from worker", ConsoleLog.Entries[0].Message);
        }

        [Test]
        public void Receive_OnMainThread_WritesQueuedMessagesFirst()
        {
            RunOnOtherThread(() => UnityLogForwarder.Receive("earlier", "", LogType.Log));

            UnityLogForwarder.Receive("later", "", LogType.Log);

            Assert.AreEqual(2, ConsoleLog.Entries.Count);
            Assert.AreEqual("earlier", ConsoleLog.Entries[0].Message);
            Assert.AreEqual("later", ConsoleLog.Entries[1].Message);
        }

        [Test]
        public void Receive_FromAConsoleSubscriber_DoesNotRecurse()
        {
            void OnAdded(ConsoleLog.LogEntry _) => UnityLogForwarder.Receive("nested", "", LogType.Log);
            ConsoleLog.OnLogAdded += OnAdded;
            try
            {
                UnityLogForwarder.Receive("outer", "", LogType.Log);
            }
            finally
            {
                ConsoleLog.OnLogAdded -= OnAdded;
            }

            Assert.AreEqual(1, ConsoleLog.Entries.Count);
            Assert.AreEqual("outer", ConsoleLog.Entries[0].Message);
        }

        private static void RunOnOtherThread(System.Action action)
        {
            var thread = new Thread(() => action());
            thread.Start();
            thread.Join();
        }
    }
}
