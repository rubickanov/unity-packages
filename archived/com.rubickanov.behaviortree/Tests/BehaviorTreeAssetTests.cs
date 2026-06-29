using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BehaviorTreeAssetTests
    {
        private readonly List<BehaviorTreeAsset> _createdAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _createdAssets)
                UnityEngine.Object.DestroyImmediate(asset);
            _createdAssets.Clear();
        }

        private BehaviorTreeAsset MakeAsset(BTNode? root)
        {
            var asset = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            SetPrivate(asset, "_root", root);
            _createdAssets.Add(asset);
            return asset;
        }

        private static BTContext MakeContext() =>
            new BTContext(owner: null, blackboard: new Blackboard(), deltaTime: 0f, time: 0f, tick: 0);

        private static void SetPrivate(object target, string name, object? value)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"{target.GetType().Name} must have a private field '{name}' — rename detected?");
            f!.SetValue(target, value);
        }

        [Test]
        public void CreateInstance_NullRoot_ReturnsNull()
        {
            var asset = MakeAsset(root: null);

            Assert.IsNull(asset.CreateInstance());
        }

        [Test]
        public void CreateInstance_DeepClonesTree_PreservesStructure()
        {
            // sequence -> [ inverter -> leaf, leaf ]
            var leafA = new StubLeaf { NextStatus = BTStatus.Success };
            var leafB = new StubLeaf { NextStatus = BTStatus.Failure };
            var inverter = new BTInverter(leafA);
            var sequence = new BTSequence(inverter, leafB);
            var asset = MakeAsset(sequence);

            var clone = asset.CreateInstance();

            Assert.IsInstanceOf<BTSequence>(clone);
            var cloneSequence = (BTSequence)clone!;
            var cloneChildren = GetChildren(cloneSequence);
            Assert.AreEqual(2, cloneChildren.Length);
            Assert.IsInstanceOf<BTInverter>(cloneChildren[0]);
            Assert.IsInstanceOf<StubLeaf>(cloneChildren[1]);

            var cloneInverter = (BTInverter)cloneChildren[0];
            var cloneInverterChild = GetChild(cloneInverter);
            Assert.IsInstanceOf<StubLeaf>(cloneInverterChild);
        }

        [Test]
        public void CreateInstance_GeneratesFreshGuidsForEveryNode()
        {
            var leaf = new StubLeaf { Guid = "leaf-src" };
            var inverter = new BTInverter(leaf) { Guid = "inv-src" };
            var sequence = new BTSequence(inverter) { Guid = "seq-src" };
            var asset = MakeAsset(sequence);

            var clone = (BTSequence)asset.CreateInstance()!;
            var cloneInverter = (BTInverter)GetChildren(clone)[0];
            var cloneLeaf = (StubLeaf)GetChild(cloneInverter)!;

            Assert.AreNotEqual("seq-src", clone.Guid);
            Assert.AreNotEqual("inv-src", cloneInverter.Guid);
            Assert.AreNotEqual("leaf-src", cloneLeaf.Guid);
        }

        [Test]
        public void CreateInstance_ProducesIndependentCloneInstances()
        {
            var leaf = new StubLeaf { NextStatus = BTStatus.Success };
            var asset = MakeAsset(new BTSequence(leaf));

            var cloneA = (BTSequence)asset.CreateInstance()!;
            var cloneB = (BTSequence)asset.CreateInstance()!;

            Assert.AreNotSame(cloneA, cloneB);
            Assert.AreNotSame(GetChildren(cloneA)[0], GetChildren(cloneB)[0]);
        }

        [Test]
        public void CreateInstance_ClonedTreeStateIsIsolatedFromSource()
        {
            // Mutate the clone's leaf and confirm the source asset's leaf is untouched.
            // Guards the "multiple agents share the asset without state interference"
            // promise in the README.
            var sourceLeaf = new StubLeaf { NextStatus = BTStatus.Success };
            var asset = MakeAsset(new BTSequence(sourceLeaf));

            var clone = (BTSequence)asset.CreateInstance()!;
            var cloneLeaf = (StubLeaf)GetChildren(clone)[0];
            cloneLeaf.NextStatus = BTStatus.Failure;
            cloneLeaf.Tick(MakeContext());

            Assert.AreEqual(BTStatus.Success, sourceLeaf.NextStatus);
            Assert.AreEqual(0, sourceLeaf.TickCount);
            Assert.AreEqual(1, cloneLeaf.TickCount);
        }

        private static BTNode[] GetChildren(BTComposite composite)
        {
            var f = typeof(BTComposite).GetField("Children", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "BTComposite must have a private field 'Children' — rename detected?");
            return (BTNode[])f!.GetValue(composite)!;
        }

        private static BTNode? GetChild(BTDecorator decorator)
        {
            var f = typeof(BTDecorator).GetField("Child", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "BTDecorator must have a private field 'Child' — rename detected?");
            return (BTNode?)f!.GetValue(decorator);
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
