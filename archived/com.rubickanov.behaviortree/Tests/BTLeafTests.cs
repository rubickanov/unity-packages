using System;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BTLeafTests
    {
        private static BTContext MakeContext(object? owner = null) =>
            new BTContext(owner, new Blackboard(), deltaTime: 0.016f, time: 2.5f, tick: 42);

        // ---- BTAction ----------------------------------------------------------

        [Test]
        public void BTAction_Tick_InvokesDelegateWithContext()
        {
            BTContext captured = default;
            var action = new BTAction(ctx =>
            {
                captured = ctx;
                return BTStatus.Success;
            });

            var owner = new object();
            action.Tick(MakeContext(owner));

            Assert.AreSame(owner, captured.Owner);
            Assert.AreEqual(0.016f, captured.DeltaTime);
            Assert.AreEqual(2.5f, captured.Time);
            Assert.AreEqual(42u, captured.Tick);
        }

        [Test]
        public void BTAction_Tick_ReturnsDelegateResult()
        {
            var action = new BTAction(_ => BTStatus.Running);

            Assert.AreEqual(BTStatus.Running, action.Tick(MakeContext()));
        }

        // ---- BTCondition -------------------------------------------------------

        [Test]
        public void BTCondition_Tick_PredicateTrue_ReturnsSuccess()
        {
            var condition = new BTCondition(_ => true);

            Assert.AreEqual(BTStatus.Success, condition.Tick(MakeContext()));
        }

        [Test]
        public void BTCondition_Tick_PredicateFalse_ReturnsFailure()
        {
            var condition = new BTCondition(_ => false);

            Assert.AreEqual(BTStatus.Failure, condition.Tick(MakeContext()));
        }

        // ---- BTLeafAction ------------------------------------------------------

        [Test]
        public void BTLeafAction_Tick_DispatchesToOnExecute()
        {
            var leaf = new RecordingLeafAction { NextStatus = BTStatus.Running };

            var status = leaf.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Running, status);
            Assert.AreEqual(1, leaf.ExecuteCount);
        }

        // ---- BTLeafCondition ---------------------------------------------------

        [Test]
        public void BTLeafCondition_Tick_OnEvaluateTrue_ReturnsSuccess()
        {
            var leaf = new RecordingLeafCondition { Result = true };

            Assert.AreEqual(BTStatus.Success, leaf.Tick(MakeContext()));
            Assert.AreEqual(1, leaf.EvaluateCount);
        }

        [Test]
        public void BTLeafCondition_Tick_OnEvaluateFalse_ReturnsFailure()
        {
            var leaf = new RecordingLeafCondition { Result = false };

            Assert.AreEqual(BTStatus.Failure, leaf.Tick(MakeContext()));
            Assert.AreEqual(1, leaf.EvaluateCount);
        }

        [Serializable]
        private class RecordingLeafAction : BTLeafAction
        {
            public BTStatus NextStatus;
            public int ExecuteCount;

            protected override BTStatus OnExecute(BTContext ctx)
            {
                ExecuteCount++;
                return NextStatus;
            }
        }

        [Serializable]
        private class RecordingLeafCondition : BTLeafCondition
        {
            public bool Result;
            public int EvaluateCount;

            protected override bool OnEvaluate(BTContext ctx)
            {
                EvaluateCount++;
                return Result;
            }
        }
    }
}
