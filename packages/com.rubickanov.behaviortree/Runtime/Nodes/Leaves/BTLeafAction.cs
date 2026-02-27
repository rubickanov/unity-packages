using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Serializable leaf action. Subclass and override <see cref="OnExecute"/> to implement.
    /// </summary>
    [Serializable]
    public abstract class BTLeafAction : BTNode
    {
        protected sealed override BTStatus OnTick(BTContext ctx) => OnExecute(ctx);

        protected abstract BTStatus OnExecute(BTContext ctx);
    }
}