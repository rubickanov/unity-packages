using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BehaviorTreeRunnerTests
    {
        private GameObject _gameObject;
        private BehaviorTreeRunner _runner;
        private readonly List<BehaviorTreeAsset> _createdAssets = new();

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(BehaviorTreeRunnerTests));
            _runner = _gameObject.AddComponent<BehaviorTreeRunner>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
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

        private static void SetPrivate(object target, string name, object? value)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"{target.GetType().Name} must have a private field '{name}' — rename detected?");
            f!.SetValue(target, value);
        }

        [Test]
        public void Initialize_SetsRuntimeRoot()
        {
            var root = new StubLeaf();

            _runner.Initialize(root);

            Assert.AreSame(root, _runner.RuntimeRoot);
        }

        [Test]
        public void Tick_WithoutRoot_LogsErrorAndDoesNotThrow()
        {
            // Runner logs via Debug.LogError at BehaviorTreeRunner.cs:45. We swap in
            // a CapturingLogHandler instead of using LogAssert.Expect because
            // LogAssert.Expect still leaks red lines into the Unity Console after a
            // green run — same rationale as ReplicationScannerTests in acs.netcode.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                Assert.DoesNotThrow(() => _runner.Tick(owner: this, deltaTime: 0.016f));
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("root is null")),
                "Runner must emit a LogError when Tick is called with no root assigned");
        }

        [Test]
        public void Tick_AccumulatesTimeAcrossCalls()
        {
            var root = new ContextCapturingStub();
            _runner.Initialize(root);

            _runner.Tick(owner: this, deltaTime: 0.5f);
            var t1 = root.LastTime;
            _runner.Tick(owner: this, deltaTime: 0.5f);
            var t2 = root.LastTime;
            _runner.Tick(owner: this, deltaTime: 0.5f);
            var t3 = root.LastTime;

            Assert.AreEqual(0.5f, t1);
            Assert.AreEqual(1.0f, t2);
            Assert.AreEqual(1.5f, t3);
        }

        [Test]
        public void Tick_PassesOwnerDeltaTimeBlackboardAndTickIntoContext()
        {
            var root = new ContextCapturingStub();
            _runner.Initialize(root);

            var owner = new object();
            _runner.Tick(owner, deltaTime: 0.125f, tick: 99);

            Assert.AreSame(owner, root.LastOwner);
            Assert.AreEqual(0.125f, root.LastDeltaTime);
            Assert.AreEqual(99u, root.LastTick);
            Assert.AreSame(_runner.Blackboard, root.LastBlackboard);
        }

        [Test]
        public void Tick_AutoInitializesFromAsset_WhenRootNotSet()
        {
            // EnsureInitialized must kick in on first Tick if _treeAsset is assigned.
            var assetRoot = new StubLeaf { NextStatus = BTStatus.Success };
            var asset = MakeAsset(assetRoot);
            SetPrivate(_runner, "_treeAsset", asset);

            _runner.Tick(owner: this, deltaTime: 0.016f);

            Assert.IsNotNull(_runner.RuntimeRoot);
            // The runtime root is a clone of assetRoot — not the same instance.
            Assert.AreNotSame(assetRoot, _runner.RuntimeRoot);
            Assert.IsInstanceOf<StubLeaf>(_runner.RuntimeRoot);
        }

        [Test]
        public void EnsureInitialized_ClonesFromAssetWhenRootMissing()
        {
            var assetRoot = new StubLeaf();
            var asset = MakeAsset(assetRoot);
            SetPrivate(_runner, "_treeAsset", asset);

            _runner.EnsureInitialized();

            Assert.IsNotNull(_runner.RuntimeRoot);
            Assert.AreNotSame(assetRoot, _runner.RuntimeRoot);
        }

        [Test]
        public void EnsureInitialized_DoesNotOverwriteExistingRoot()
        {
            var manualRoot = new StubLeaf();
            var assetRoot = new StubLeaf();
            var asset = MakeAsset(assetRoot);
            SetPrivate(_runner, "_treeAsset", asset);
            _runner.Initialize(manualRoot);

            _runner.EnsureInitialized();

            Assert.AreSame(manualRoot, _runner.RuntimeRoot);
        }

        [Test]
        public void Blackboard_IsSharedAcrossTicks()
        {
            var key = new BlackboardKey<int>("counter");
            var root = new BlackboardWritingStub(key);
            _runner.Initialize(root);

            _runner.Tick(owner: this, deltaTime: 0f);
            _runner.Tick(owner: this, deltaTime: 0f);
            _runner.Tick(owner: this, deltaTime: 0f);

            Assert.AreEqual(3, _runner.Blackboard.Get(key));
        }

        [Serializable]
        private class StubLeaf : BTLeafAction
        {
            public BTStatus NextStatus = BTStatus.Success;
            protected override BTStatus OnExecute(BTContext ctx) => NextStatus;
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

        private class BlackboardWritingStub : BTLeafAction
        {
            private readonly BlackboardKey<int> _key;
            public BlackboardWritingStub(BlackboardKey<int> key) { _key = key; }

            protected override BTStatus OnExecute(BTContext ctx)
            {
                var next = ctx.Blackboard.TryGet(_key, out var cur) ? cur + 1 : 1;
                ctx.Blackboard.Set(_key, next);
                return BTStatus.Success;
            }
        }

        // Swaps in as Debug.unityLogger.logHandler while a negative test runs, so
        // expected Debug.LogError calls are captured for assertion but never reach
        // Unity's native logger — keeps the Console clean after the run.
        private sealed class CapturingLogHandler : ILogHandler
        {
            private readonly List<(LogType type, string message)> _captured = new();
            public IReadOnlyList<(LogType type, string message)> Captured => _captured;

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                _captured.Add((logType, string.Format(format, args)));
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                _captured.Add((LogType.Exception, exception.Message));
            }
        }
    }
}
