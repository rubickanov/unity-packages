using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Leaf node that executes a delegate. For code-built trees; use <see cref="BTLeafAction"/> for serializable nodes.
    /// </summary>
    public class BTAction : BTNode
    {
        private readonly Func<BTContext, BTStatus> _action;

        public BTAction(Func<BTContext, BTStatus> action)
        {
            _action = action;
        }

        protected override BTStatus OnTick(BTContext ctx)
        {
            return _action(ctx);
        }
    }
}