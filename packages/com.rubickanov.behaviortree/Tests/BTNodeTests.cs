using System;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BTNodeTests
    {
        private static BTContext MakeContext() =>
            new BTContext(owner: null, blackboard: new Blackboard(), deltaTime: 0f, time: 0f, tick: 0);

        [Test]
        public void Tick_CachesLastStatusAsSuccess()
        {
            var node = new StubLeaf { NextStatus = BTStatus.Success };

            node.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Success, node.LastStatus);
        }

        [Test]
        public void Tick_CachesLastStatusAsFailure()
        {
            var node = new StubLeaf { NextStatus = BTStatus.Failure };

            node.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Failure, node.LastStatus);
        }

        [Test]
        public void Tick_CachesLastStatusAsRunning()
        {
            var node = new StubLeaf { NextStatus = BTStatus.Running };

            node.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Running, node.LastStatus);
        }

        [Test]
        public void Tick_ReturnsSameStatusAsOnTick()
        {
            var node = new StubLeaf { NextStatus = BTStatus.Running };

            var status = node.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Running, status);
        }

        [Test]
        public void Abort_Default_SetsLastStatusToFailure()
        {
            var node = new StubLeaf { NextStatus = BTStatus.Success };
            node.Tick(MakeContext());

            node.Abort();

            Assert.AreEqual(BTStatus.Failure, node.LastStatus);
        }

        [Test]
        public void Clone_GeneratesNewGuid()
        {
            var node = new StubLeaf { Guid = "original-guid" };

            var clone = node.Clone();

            Assert.AreNotEqual("original-guid", clone.Guid);
            Assert.IsNotNull(clone.Guid);
            Assert.IsNotEmpty(clone.Guid);
        }

        [Test]
        public void Clone_OnLeaf_ProducesIndependentInstance()
        {
            var node = new StubLeaf { NextStatus = BTStatus.Success };

            var clone = (StubLeaf)node.Clone();
            clone.NextStatus = BTStatus.Failure;

            Assert.AreEqual(BTStatus.Success, node.NextStatus);
            Assert.AreEqual(BTStatus.Failure, clone.NextStatus);
        }

        [Serializable]
        private class StubLeaf : BTLeafAction
        {
            public BTStatus NextStatus;
            protected override BTStatus OnExecute(BTContext ctx) => NextStatus;
        }
    }
}
