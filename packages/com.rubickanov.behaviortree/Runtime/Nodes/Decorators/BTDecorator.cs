using System;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Base class for nodes with a single child that modify its behavior or result.
    /// </summary>
    [Serializable]
    public abstract class BTDecorator : BTNode
    {
        [SerializeReference] protected BTNode? Child;

        protected BTDecorator() { }

        protected BTDecorator(BTNode child)
        {
            Child = child;
        }

        public override void Abort()
        {
            base.Abort();
            Child?.Abort();
        }

        public override BTNode Clone()
        {
            var clone = (BTDecorator)base.Clone();
            clone.Child = Child?.Clone();
            return clone;
        }
    }
}