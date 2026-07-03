using System;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BTInverterTests
    {
        private static BTContext MakeContext() =>
            new BTContext(owner: null, blackboard: new Blackboard(), deltaTime: 0f, time: 0f, tick: 0);

        [Test]
        public void Tick_ChildSuccess_ReturnsFailure()
        {
            var inverter = new BTInverter(new StubLeaf { NextStatus = BTStatus.Success });

            Assert.AreEqual(BTStatus.Failure, inverter.Tick(MakeContext()));
        }

        [Test]
        public void Tick_ChildFailure_ReturnsSuccess()
        {
            var inverter = new BTInverter(new StubLeaf { NextStatus = BTStatus.Failure });

            Assert.AreEqual(BTStatus.Success, inverter.Tick(MakeContext()));
        }

        [Test]
        public void Tick_ChildRunning_ReturnsRunning()
        {
            var inverter = new BTInverter(new StubLeaf { NextStatus = BTStatus.Running });

            Assert.AreEqual(BTStatus.Running, inverter.Tick(MakeContext()));
        }

        [Test]
        public void Tick_NullChild_ReturnsFailure()
        {
            var inverter = new BTInverter();

            Assert.AreEqual(BTStatus.Failure, inverter.Tick(MakeContext()));
        }

        [Test]
        public void Abort_PropagatesToChild()
        {
            var child = new StubLeaf();
            var inverter = new BTInverter(child);

            inverter.Abort();

            Assert.AreEqual(1, child.AbortCount);
        }

        [Serializable]
        private class StubLeaf : BTLeafAction
        {
            public BTStatus NextStatus = BTStatus.Success;
            public int AbortCount;

            protected override BTStatus OnExecute(BTContext ctx) => NextStatus;

            public override void Abort()
            {
                base.Abort();
                AbortCount++;
            }
        }
    }
}
