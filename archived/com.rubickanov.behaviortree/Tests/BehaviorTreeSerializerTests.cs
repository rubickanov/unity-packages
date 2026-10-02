using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Editor;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BehaviorTreeSerializerTests
    {
        private readonly List<BehaviorTreeAsset> _assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _assets)
                UnityEngine.Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        private BehaviorTreeSerializer MakeSerializer()
        {
            var asset = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            _assets.Add(asset);
            return new BehaviorTreeSerializer(new SerializedObject(asset));
        }

        private BehaviorTreeAsset MakeAsset(BTNode? root)
        {
            var asset = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
            SetPrivate(asset, "_root", root);
            _assets.Add(asset);
            return asset;
        }

        private static void SetPrivate(object target, string name, object? value)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"{target.GetType().Name} must have a private field '{name}' — rename detected?");
            f!.SetValue(target, value);
        }

        private static bool HasPair(IReadOnlyList<(string parentGuid, string childGuid)> pairs, string parent, string child)
            => pairs.Any(p => p.parentGuid == parent && p.childGuid == child);

        [Test]
        public void CreateNode_FirstNode_BecomesRoot()
        {
            var serializer = MakeSerializer();

            var guid = serializer.CreateNode(typeof(BTSelector), Vector2.zero);

            Assert.AreEqual(guid, serializer.GetRootGuid());
            Assert.AreEqual(1, serializer.GetAllNodes().Count);
        }

        [Test]
        public void CreateNode_SecondNode_GoesToOrphans()
        {
            var serializer = MakeSerializer();
            serializer.CreateNode(typeof(BTSelector), Vector2.zero);

            var second = serializer.CreateNode(typeof(BTSequence), Vector2.zero);

            var info = serializer.GetAllNodes().First(n => n.Guid == second);
            Assert.IsTrue(info.IsOrphan);
        }

        [Test]
        public void AddChild_AttachesOrphanUnderParent_AndClearsOrphanFlag()
        {
            var serializer = MakeSerializer();
            var parent = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var child = serializer.CreateNode(typeof(BTSequence), Vector2.zero);

            serializer.AddChild(parent!, child!);

            Assert.IsTrue(HasPair(serializer.GetParentChildPairs(), parent!, child!));
            var info = serializer.GetAllNodes().First(n => n.Guid == child);
            Assert.IsFalse(info.IsOrphan);
        }

        [Test]
        public void AddChild_Reparent_RemovesChildFromPreviousParent()
        {
            var serializer = MakeSerializer();
            var a = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var b = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var c = serializer.CreateNode(typeof(BTSequence), Vector2.zero);
            serializer.AddChild(a!, c!);

            serializer.AddChild(b!, c!);

            var pairs = serializer.GetParentChildPairs();
            Assert.IsTrue(HasPair(pairs, b!, c!), "child must be under the new parent");
            Assert.IsFalse(HasPair(pairs, a!, c!), "child must be detached from the old parent");
            CollectionAssert.DoesNotContain(serializer.GetChildGuids(a!), c);
            CollectionAssert.Contains(serializer.GetChildGuids(b!), c);
        }

        [Test]
        public void DeleteNode_Root_ClearsRootAndKeepsChildrenAsOrphans()
        {
            var serializer = MakeSerializer();
            var root = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var child = serializer.CreateNode(typeof(BTSequence), Vector2.zero);
            serializer.AddChild(root!, child!);

            serializer.DeleteNode(root!);

            Assert.IsNull(serializer.GetRootGuid());
            var info = serializer.GetAllNodes().First(n => n.Guid == child);
            Assert.IsTrue(info.IsOrphan, "the deleted node's child must survive as an orphan");
        }

        [Test]
        public void WouldCreateCycle_SelfReference_ReturnsTrue()
        {
            var serializer = MakeSerializer();
            var a = serializer.CreateNode(typeof(BTSelector), Vector2.zero);

            Assert.IsTrue(serializer.WouldCreateCycle(a!, a!));
        }

        [Test]
        public void WouldCreateCycle_DirectBackEdge_ReturnsTrue()
        {
            var serializer = MakeSerializer();
            var a = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var b = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            serializer.AddChild(a!, b!);

            // a -> b exists; adding b -> a would close a loop.
            Assert.IsTrue(serializer.WouldCreateCycle(b!, a!));
        }

        [Test]
        public void WouldCreateCycle_TransitiveBackEdge_ReturnsTrue()
        {
            var serializer = MakeSerializer();
            var a = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var b = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var c = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            serializer.AddChild(a!, b!);
            serializer.AddChild(b!, c!);

            // a -> b -> c exists; adding c -> a would close a loop.
            Assert.IsTrue(serializer.WouldCreateCycle(c!, a!));
        }

        [Test]
        public void WouldCreateCycle_UnrelatedNodes_ReturnsFalse()
        {
            var serializer = MakeSerializer();
            var a = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var b = serializer.CreateNode(typeof(BTSelector), Vector2.zero);

            Assert.IsFalse(serializer.WouldCreateCycle(a!, b!));
        }

        [Test]
        public void SortChildren_OrdersChildrenByXPosition()
        {
            var serializer = MakeSerializer();
            var parent = serializer.CreateNode(typeof(BTSelector), Vector2.zero);
            var left = serializer.CreateNode(typeof(BTSequence), Vector2.zero);
            var right = serializer.CreateNode(typeof(BTSequence), Vector2.zero);
            // Attach in reversed visual order: right first, then left.
            serializer.AddChild(parent!, right!);
            serializer.AddChild(parent!, left!);
            serializer.SetPositionBatch(new Dictionary<string, Vector2>
            {
                [left!] = new Vector2(0f, 100f),
                [right!] = new Vector2(500f, 100f),
            });

            serializer.SortChildren(parent!);

            var children = serializer.GetChildGuids(parent!);
            Assert.AreEqual(new[] { left, right }, children.ToArray());
        }

        [Test]
        public void HasSubtreeCycle_MutualReference_ReturnsTrue()
        {
            var assetA = MakeAsset(root: null);
            var subtreeToA = new BTSubtree();
            SetPrivate(subtreeToA, "_subtreeAsset", assetA);
            var assetB = MakeAsset(subtreeToA);

            // assetA references assetB; assetB references assetA back.
            Assert.IsTrue(BehaviorTreeSerializer.HasSubtreeCycle(assetA, assetB));
        }

        [Test]
        public void HasSubtreeCycle_NoBackReference_ReturnsFalse()
        {
            var assetA = MakeAsset(root: null);
            var assetB = MakeAsset(new StubLeaf());

            Assert.IsFalse(BehaviorTreeSerializer.HasSubtreeCycle(assetA, assetB));
        }

        [Serializable]
        private class StubLeaf : BTLeafAction
        {
            protected override BTStatus OnExecute(BTContext ctx) => BTStatus.Success;
        }
    }
}
