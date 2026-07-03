using System;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Runtime
{
    [Serializable]
    [BTNodeDescription("Subtree", "Subtree", "Executes another BehaviorTree asset as a subtree.")]
    public class BTSubtree : BTNode
    {
        [SerializeField] private BehaviorTreeAsset? _subtreeAsset;

        [NonSerialized] private BTNode? _runtimeRoot;

        public BehaviorTreeAsset? SubtreeAsset => _subtreeAsset;

        protected override BTStatus OnTick(BTContext ctx)
        {
            if (_subtreeAsset == null)
                return BTStatus.Failure;

            _runtimeRoot ??= _subtreeAsset.CreateInstance();

            if (_runtimeRoot == null)
                return BTStatus.Failure;

            return _runtimeRoot.Tick(ctx);
        }

        public override void Abort()
        {
            base.Abort();
            _runtimeRoot?.Abort();
        }

        public override BTNode Clone()
        {
            var clone = (BTSubtree)base.Clone();
            clone._runtimeRoot = null;
            return clone;
        }
    }
}