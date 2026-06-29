using System;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BTCooldownTests
    {
        private static BTContext ContextAt(float time) =>
            new BTContext(owner: null, blackboard: new Blackboard(), deltaTime: 0f, time: time, tick: 0);

        [Test]
        public void Tick_FirstCall_TicksChildAndReturnsChildStatus()
        {
            var child = new StubLeaf { NextStatus = BTStatus.Success };
            var cooldown = new BTCooldown(1f, child);

            var status = cooldown.Tick(ContextAt(0f));

            Assert.AreEqual(BTStatus.Success, status);
            Assert.AreEqual(1, child.TickCount);
        }

        [Test]
        public void Tick_WithinCooldownWindow_ReturnsFailureAndDoesNotTickChild()
        {
            var child = new StubLeaf { NextStatus = BTStatus.Success };
            var cooldown = new BTCooldown(1f, child);
            cooldown.Tick(ContextAt(0f));

            var status = cooldown.Tick(ContextAt(0.5f));

            Assert.AreEqual(BTStatus.Failure, status);
            Assert.AreEqual(1, child.TickCount, "child must not be ticked while cooldown is active");
        }

        [Test]
        public void Tick_AfterCooldownExpires_TicksChildAgain()
        {
            var child = new StubLeaf { NextStatus = BTStatus.Success };
            var cooldown = new BTCooldown(1f, child);
            cooldown.Tick(ContextAt(0f));

            var status = cooldown.Tick(ContextAt(1.01f));

            Assert.AreEqual(BTStatus.Success, status);
            Assert.AreEqual(2, child.TickCount);
        }

        [Test]
        public void Tick_ChildReturnsRunning_DoesNotStartCooldown()
        {
            // BTCooldown arms the gate only when the child completes (Success/Failure).
            // Running keeps the child live, so the very next tick must still pass through.
            var child = new StubLeaf { NextStatus = BTStatus.Running };
            var cooldown = new BTCooldown(1f, child);

            cooldown.Tick(ContextAt(0f));
            var status = cooldown.Tick(ContextAt(0.1f));

            Assert.AreEqual(BTStatus.Running, status);
            Assert.AreEqual(2, child.TickCount);
        }

        [Test]
        public void Tick_ChildFailure_StillStartsCooldown()
        {
            // Failure counts as "completion" — cooldown must arm just like on Success.
            var child = new StubLeaf { NextStatus = BTStatus.Failure };
            var cooldown = new BTCooldown(1f, child);
            cooldown.Tick(ContextAt(0f));

            var status = cooldown.Tick(ContextAt(0.5f));

            Assert.AreEqual(BTStatus.Failure, status);
            Assert.AreEqual(1, child.TickCount, "within cooldown window child must not be re-ticked");
        }

        [Test]
        public void Tick_NullChild_ReturnsFailure()
        {
            var cooldown = new BTCooldown();

            Assert.AreEqual(BTStatus.Failure, cooldown.Tick(ContextAt(0f)));
        }

        [Test]
        public void Abort_ResetsCooldown_AllowsImmediateReEntry()
        {
            // After Abort, the child must be reachable immediately regardless of the
            // previously-armed _readyAt — critical for pre-emption scenarios where a
            // selector yanks the cooldown branch and the AI needs it usable right now.
            var child = new StubLeaf { NextStatus = BTStatus.Success };
            var cooldown = new BTCooldown(100f, child);
            cooldown.Tick(ContextAt(0f));

            cooldown.Abort();
            var status = cooldown.Tick(ContextAt(0.1f));

            Assert.AreEqual(BTStatus.Success, status);
            Assert.AreEqual(2, child.TickCount);
        }

        [Serializable]
        private class StubLeaf : BTLeafAction
        {
            public BTStatus NextStatus = BTStatus.Success;
            public int TickCount;

            protected override BTStatus OnExecute(BTContext ctx)
            {
                TickCount++;
                return NextStatus;
            }
        }
    }
}
