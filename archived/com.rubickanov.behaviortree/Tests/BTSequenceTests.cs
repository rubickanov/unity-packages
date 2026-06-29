using System;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BTSequenceTests
    {
        private static BTContext MakeContext() =>
            new BTContext(owner: null, blackboard: new Blackboard(), deltaTime: 0f, time: 0f, tick: 0);

        [Test]
        public void Tick_AllChildrenSucceed_ReturnsSuccess()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Success };
            var c = new StubLeaf { NextStatus = BTStatus.Success };
            var sequence = new BTSequence(a, b, c);

            var status = sequence.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Success, status);
            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(1, b.TickCount);
            Assert.AreEqual(1, c.TickCount);
        }

        [Test]
        public void Tick_FirstChildFails_ReturnsFailureAndSkipsRest()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Success };
            var sequence = new BTSequence(a, b);

            var status = sequence.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Failure, status);
            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(0, b.TickCount);
        }

        [Test]
        public void Tick_MiddleChildFails_SkipsLaterChildren()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Failure };
            var c = new StubLeaf { NextStatus = BTStatus.Success };
            var sequence = new BTSequence(a, b, c);

            var status = sequence.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Failure, status);
            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(1, b.TickCount);
            Assert.AreEqual(0, c.TickCount);
        }

        [Test]
        public void Tick_ChildReturnsRunning_ReturnsRunningAndStopsAtThatChild()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var c = new StubLeaf { NextStatus = BTStatus.Success };
            var sequence = new BTSequence(a, b, c);

            var status = sequence.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Running, status);
            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(1, b.TickCount);
            Assert.AreEqual(0, c.TickCount);
        }

        [Test]
        public void Tick_ResumesFromRunningChildOnNextTick()
        {
            // First tick: A succeeds, B runs. Second tick must NOT re-enter A; it must
            // resume at B. This is the core sequence-resumption contract.
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var sequence = new BTSequence(a, b);

            sequence.Tick(MakeContext());
            b.NextStatus = BTStatus.Success;
            sequence.Tick(MakeContext());

            Assert.AreEqual(1, a.TickCount, "A must not be re-ticked after it already succeeded");
            Assert.AreEqual(2, b.TickCount);
        }

        [Test]
        public void Tick_FullSuccessCycle_RestartsFromFirstChildOnNextTick()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Success };
            var sequence = new BTSequence(a, b);

            sequence.Tick(MakeContext());
            sequence.Tick(MakeContext());

            Assert.AreEqual(2, a.TickCount);
            Assert.AreEqual(2, b.TickCount);
        }

        [Test]
        public void Tick_FailureAfterRunning_ResetsIndexToZero()
        {
            // A Success then B Running arms _currentIndex=1. Next tick B fails — the
            // sequence must fall back to the beginning, not stay stuck at index 1.
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var sequence = new BTSequence(a, b);

            sequence.Tick(MakeContext());
            b.NextStatus = BTStatus.Failure;
            sequence.Tick(MakeContext());
            b.NextStatus = BTStatus.Success;
            sequence.Tick(MakeContext());

            // Third tick must start from A, proving the failure reset the index.
            Assert.AreEqual(2, a.TickCount);
            Assert.AreEqual(3, b.TickCount);
        }

        [Test]
        public void Abort_ResetsResumptionIndex()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var sequence = new BTSequence(a, b);
            sequence.Tick(MakeContext());

            sequence.Abort();
            b.NextStatus = BTStatus.Success;
            sequence.Tick(MakeContext());

            Assert.AreEqual(2, a.TickCount, "after Abort, next tick must restart from A");
        }

        [Test]
        public void Abort_PropagatesToAllChildren()
        {
            var a = new StubLeaf();
            var b = new StubLeaf();
            var sequence = new BTSequence(a, b);

            sequence.Abort();

            Assert.AreEqual(1, a.AbortCount);
            Assert.AreEqual(1, b.AbortCount);
        }

        [Test]
        public void Tick_EmptyChildren_ReturnsSuccess()
        {
            // Vacuous success: no children means the loop never executes and the
            // trailing return Success fires. This is a documented edge case of the
            // "all children succeeded" contract.
            var sequence = new BTSequence();

            var status = sequence.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Success, status);
        }

        [Serializable]
        private class StubLeaf : BTLeafAction
        {
            public BTStatus NextStatus = BTStatus.Success;
            public int TickCount;
            public int AbortCount;

            protected override BTStatus OnExecute(BTContext ctx)
            {
                TickCount++;
                return NextStatus;
            }

            public override void Abort()
            {
                base.Abort();
                AbortCount++;
            }
        }
    }
}
