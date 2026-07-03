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
        public void Clone_PreservesGuid()
        {
            // Runtime clones keep the source GUID so the editor can map runtime nodes
            // back to their asset counterparts for live play-mode visualization. Fresh
            // GUIDs are the job of ShallowClone (editor copy/paste).
            var node = new StubLeaf { Guid = "original-guid" };

            var clone = node.Clone();

            Assert.AreEqual("original-guid", clone.Guid);
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
