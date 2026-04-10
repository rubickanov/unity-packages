using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BTSubtreeTests
    {
        private readonly List<BehaviorTreeAsset> _createdAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _createdAssets)
                UnityEngine.Object.DestroyImmediate(asset);
            _createdAssets.Clear();
        }

        private BehaviorTreeAsset MakeAsset(BTNode root)
        {
            var asset = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            SetPrivate(asset, "_root", root);
            _createdAssets.Add(asset);
            return asset;
        }

        private static BTContext MakeContext() =>
            new BTContext(owner: null, blackboard: new Blackboard(), deltaTime: 0f, time: 0f, tick: 0);

        private static void SetPrivate(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            // If a refactor renames this field the whole suite should fail loudly.
            Assert.IsNotNull(f, $"{target.GetType().Name} must have a private field '{name}' — rename detected?");
            f.SetValue(target, value);
        }

        [Test]
        public void Tick_NullAsset_ReturnsFailure()
        {
            var subtree = new BTSubtree();

            Assert.AreEqual(BTStatus.Failure, subtree.Tick(MakeContext()));
        }

        [Test]
        public void Tick_FirstCall_LazilyInitializesAndTicksRuntimeRoot()
        {
            var child = new StubLeaf { NextStatus = BTStatus.Success };
            var asset = MakeAsset(child);
            var subtree = new BTSubtree();
            SetPrivate(subtree, "_subtreeAsset", asset);

            var status = subtree.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Success, status);
            // The subtree tree is a clone, so the original child is untouched. What we
            // check instead is that the subtree DID tick something — its LastStatus
            // reflects the clone's result.
            Assert.AreEqual(BTStatus.Success, subtree.LastStatus);
        }

        [Test]
        public void Tick_MultipleCalls_ReuseSameRuntimeRootInstance()
        {
            // StubLeaf keeps a static call counter we can inspect post-hoc. The runtime
            // root is cloned once; if the subtree re-cloned on every tick, the counter
            // would still increment but a fresh instance would lose its own state.
            // Instead we verify via a running-state latch: first tick returns Running,
            // second tick — on the SAME runtime instance — flips to Success without a
            // new Clone happening (otherwise the latch would reset to Running).
            var child = new LatchingStub();
            var asset = MakeAsset(child);
            var subtree = new BTSubtree();
            SetPrivate(subtree, "_subtreeAsset", asset);

            var firstStatus = subtree.Tick(MakeContext());
            var secondStatus = subtree.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Running, firstStatus);
            Assert.AreEqual(BTStatus.Success, secondStatus,
                "subtree must reuse its runtime root across ticks, not re-clone");
        }

        [Test]
        public void Tick_PassesContextOwnerAndBlackboardToSubtree()
        {
            var child = new ContextCapturingStub();
            var asset = MakeAsset(child);
            var subtree = new BTSubtree();
            SetPrivate(subtree, "_subtreeAsset", asset);

            var owner = new object();
            var blackboard = new Blackboard();
            subtree.Tick(new BTContext(owner, blackboard, deltaTime: 0.25f, time: 1.5f, tick: 7));

            // The actual ticked leaf is the cloned one; we read it out of the subtree's
            // runtime root via reflection.
            var runtimeRoot = GetRuntimeRoot(subtree);
            Assert.IsInstanceOf<ContextCapturingStub>(runtimeRoot);
            var captured = (ContextCapturingStub)runtimeRoot!;
            Assert.AreSame(owner, captured.LastOwner);
            Assert.AreSame(blackboard, captured.LastBlackboard);
            Assert.AreEqual(0.25f, captured.LastDeltaTime);
            Assert.AreEqual(1.5f, captured.LastTime);
            Assert.AreEqual(7u, captured.LastTick);
        }

        [Test]
        public void Abort_PropagatesToRuntimeRoot()
        {
            var child = new StubLeaf();
            var asset = MakeAsset(child);
            var subtree = new BTSubtree();
            SetPrivate(subtree, "_subtreeAsset", asset);
            subtree.Tick(MakeContext());

            subtree.Abort();

            var runtimeRoot = (StubLeaf)GetRuntimeRoot(subtree)!;
            Assert.AreEqual(1, runtimeRoot.AbortCount);
        }

        [Test]
        public void Clone_ClearsRuntimeRoot_SoCloneInitializesIndependently()
        {
            // The clone must start fresh: if both subtrees shared the same runtime root
            // instance, two agents running the asset would stomp on each other's state.
            var child = new StubLeaf { NextStatus = BTStatus.Success };
            var asset = MakeAsset(child);
            var original = new BTSubtree();
            SetPrivate(original, "_subtreeAsset", asset);
            original.Tick(MakeContext());
            var originalRuntimeRoot = GetRuntimeRoot(original);

            var clone = (BTSubtree)original.Clone();
            var cloneRuntimeRootBeforeTick = GetRuntimeRoot(clone);
            clone.Tick(MakeContext());
            var cloneRuntimeRootAfterTick = GetRuntimeRoot(clone);

            Assert.IsNotNull(originalRuntimeRoot);
            Assert.IsNull(cloneRuntimeRootBeforeTick, "clone must start with a null runtime root");
            Assert.IsNotNull(cloneRuntimeRootAfterTick);
            Assert.AreNotSame(originalRuntimeRoot, cloneRuntimeRootAfterTick,
                "clone must use its own independent runtime root instance");
        }

        private static BTNode? GetRuntimeRoot(BTSubtree subtree)
        {
            var f = typeof(BTSubtree).GetField("_runtimeRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "BTSubtree must have a private field '_runtimeRoot' — rename detected?");
            return (BTNode?)f!.GetValue(subtree);
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

        [Serializable]
        private class LatchingStub : BTLeafAction
        {
            private int _calls;
            protected override BTStatus OnExecute(BTContext ctx)
            {
                _calls++;
                return _calls == 1 ? BTStatus.Running : BTStatus.Success;
            }
        }

        [Serializable]
        private class ContextCapturingStub : BTLeafAction
        {
            public object? LastOwner;
            public Blackboard? LastBlackboard;
            public float LastDeltaTime;
            public float LastTime;
            public uint LastTick;

            protected override BTStatus OnExecute(BTContext ctx)
            {
                LastOwner = ctx.Owner;
                LastBlackboard = ctx.Blackboard;
                LastDeltaTime = ctx.DeltaTime;
                LastTime = ctx.Time;
                LastTick = ctx.Tick;
                return BTStatus.Success;
            }
        }
    }
}
