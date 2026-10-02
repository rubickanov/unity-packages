using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    [Serializable]
    [BTNodeDescription("Inverter", "Decorators", "Inverts child result: Success becomes Failure and vice versa.")]
    public class BTInverter : BTDecorator
    {
        public BTInverter() { }
        public BTInverter(BTNode child) : base(child) { }

        protected override BTStatus OnTick(BTContext ctx)
        {
            if (Child == null)
                return BTStatus.Failure;

            var status = Child.Tick(ctx);
            return status switch
            {
                BTStatus.Success => BTStatus.Failure,
                BTStatus.Failure => BTStatus.Success,
                _ => BTStatus.Running
            };
        }
    }
}