using System;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BTSelectorTests
    {
        private static BTContext MakeContext() =>
            new BTContext(owner: null, blackboard: new Blackboard(), deltaTime: 0f, time: 0f, tick: 0);

        [Test]
        public void Tick_FirstChildSucceeds_ReturnsSuccessAndSkipsRest()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Success };
            var b = new StubLeaf { NextStatus = BTStatus.Success };
            var selector = new BTSelector(a, b);

            var status = selector.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Success, status);
            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(0, b.TickCount);
        }

        [Test]
        public void Tick_FirstFailsSecondSucceeds_ReturnsSuccess()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Success };
            var selector = new BTSelector(a, b);

            var status = selector.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Success, status);
            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(1, b.TickCount);
        }

        [Test]
        public void Tick_AllChildrenFail_ReturnsFailure()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Failure };
            var c = new StubLeaf { NextStatus = BTStatus.Failure };
            var selector = new BTSelector(a, b, c);

            var status = selector.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Failure, status);
            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(1, b.TickCount);
            Assert.AreEqual(1, c.TickCount);
        }

        [Test]
        public void Tick_ChildReturnsRunning_ReturnsRunningAndSkipsRest()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var c = new StubLeaf { NextStatus = BTStatus.Success };
            var selector = new BTSelector(a, b, c);

            var status = selector.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Running, status);
            Assert.AreEqual(1, b.TickCount);
            Assert.AreEqual(0, c.TickCount);
        }

        [Test]
        public void Tick_RunningChildStaysRunning_DoesNotAbortItself()
        {
            // Same child is still running across two ticks — selector must NOT call
            // Abort() on it between ticks.
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var selector = new BTSelector(a, b);

            selector.Tick(MakeContext());
            selector.Tick(MakeContext());

            Assert.AreEqual(0, b.AbortCount);
        }

        [Test]
        public void Tick_RunningChildSwitches_AbortsPreviousRunner()
        {
            // Tick 1: A fails, B runs → _runningIndex=1.
            // Tick 2: A now succeeds → selector returns Success at index 0, and since
            //         _runningIndex (1) != i (0), it must Abort() child B before
            //         clearing the running index.
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var selector = new BTSelector(a, b);

            selector.Tick(MakeContext());
            a.NextStatus = BTStatus.Success;
            selector.Tick(MakeContext());

            Assert.AreEqual(1, b.AbortCount, "previously running child B must be aborted when selection moves back to A");
        }

        [Test]
        public void Tick_RunningChildReachesSuccess_ClearsRunningIndex()
        {
            // B was running, next tick B returns Success. _runningIndex must be cleared
            // to -1, so a follow-up tick where B would run again does NOT spuriously
            // abort "itself" (index doesn't equal the old running index anyway, but the
            // contract is that success clears the bookkeeping).
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var selector = new BTSelector(b);

            selector.Tick(MakeContext());
            b.NextStatus = BTStatus.Success;
            selector.Tick(MakeContext());
            b.NextStatus = BTStatus.Running;
            selector.Tick(MakeContext());

            Assert.AreEqual(0, b.AbortCount);
        }

        [Test]
        public void Tick_AllFailAfterRunning_ResetsRunningIndex()
        {
            // B was running, next tick both children fail. After the failure fallthrough
            // the selector must NOT leave _runningIndex=1 dangling — a subsequent tick
            // where a different child runs should not spuriously Abort() index 1.
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var selector = new BTSelector(a, b);

            selector.Tick(MakeContext());
            b.NextStatus = BTStatus.Failure;
            selector.Tick(MakeContext());
            a.NextStatus = BTStatus.Running;
            selector.Tick(MakeContext());

            Assert.AreEqual(0, b.AbortCount);
        }

        [Test]
        public void Abort_ResetsRunningIndex()
        {
            var a = new StubLeaf { NextStatus = BTStatus.Failure };
            var b = new StubLeaf { NextStatus = BTStatus.Running };
            var selector = new BTSelector(a, b);
            selector.Tick(MakeContext());

            selector.Abort();
            // After Abort, ticking again where A now succeeds must NOT trigger an
            // extra Abort on B — the running bookkeeping was reset.
            a.NextStatus = BTStatus.Success;
            b.AbortCount = 0;
            selector.Tick(MakeContext());

            Assert.AreEqual(0, b.AbortCount);
        }

        [Test]
        public void Abort_PropagatesToAllChildren()
        {
            var a = new StubLeaf();
            var b = new StubLeaf();
            var selector = new BTSelector(a, b);

            selector.Abort();

            Assert.AreEqual(1, a.AbortCount);
            Assert.AreEqual(1, b.AbortCount);
        }

        [Test]
        public void Tick_EmptyChildren_ReturnsFailure()
        {
            var selector = new BTSelector();

            var status = selector.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Failure, status);
        }

        [Serializable]
        private class StubLeaf : BTLeafAction
        {
            public BTStatus NextStatus = BTStatus.Failure;
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
