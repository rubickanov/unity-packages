using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Serializable leaf condition. Subclass and override <see cref="OnEvaluate"/> to implement.
    /// </summary>
    [Serializable]
    public abstract class BTLeafCondition : BTNode
    {
        protected sealed override BTStatus OnTick(BTContext ctx) =>
            OnEvaluate(ctx) ? BTStatus.Success : BTStatus.Failure;

        protected abstract bool OnEvaluate(BTContext ctx);
    }
}