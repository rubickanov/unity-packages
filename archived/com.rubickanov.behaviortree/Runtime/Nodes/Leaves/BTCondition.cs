using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Leaf node that evaluates a predicate. For code-built trees; use <see cref="BTLeafCondition"/> for serializable nodes.
    /// </summary>
    public class BTCondition : BTNode
    {
        private readonly Func<BTContext, bool> _predicate;

        public BTCondition(Func<BTContext, bool> predicate)
        {
            _predicate = predicate;
        }

        protected override BTStatus OnTick(BTContext ctx)
        {
            return _predicate(ctx) ? BTStatus.Success : BTStatus.Failure;
        }
    }
}