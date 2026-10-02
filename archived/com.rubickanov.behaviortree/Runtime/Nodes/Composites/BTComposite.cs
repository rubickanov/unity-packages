using System;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Base class for nodes with multiple children (Selector, Sequence).
    /// </summary>
    [Serializable]
    public abstract class BTComposite : BTNode
    {
        [SerializeReference] protected BTNode[] Children = Array.Empty<BTNode>();

        protected BTComposite() { }

        protected BTComposite(params BTNode[] children)
        {
            Children = children;
        }

        public override void Abort()
        {
            base.Abort();
            foreach (var child in Children)
                child.Abort();
        }

        internal BTNode[] GetChildren() => Children;
        internal void SetChildren(BTNode[] children) => Children = children;

        public override BTNode Clone()
        {
            var clone = (BTComposite)base.Clone();
            clone.Children = new BTNode[Children.Length];
            for (int i = 0; i < Children.Length; i++)
                clone.Children[i] = Children[i].Clone();
            return clone;
        }
    }
}