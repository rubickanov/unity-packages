using System;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Runtime
{
    [Serializable]
    [BTNodeDescription("Cooldown", "Decorators", "Blocks child execution until cooldown duration expires.")]
    public class BTCooldown : BTDecorator
    {
        [SerializeField] private float _duration;
        [NonSerialized] private float _readyAt;

        public BTCooldown() { }

        public BTCooldown(float duration, BTNode child) : base(child)
        {
            _duration = duration;
        }

        protected override BTStatus OnTick(BTContext ctx)
        {
            if (ctx.Time < _readyAt)
                return BTStatus.Failure;

            if (Child == null)
                return BTStatus.Failure;

            var status = Child.Tick(ctx);

            if (status != BTStatus.Running)
                _readyAt = ctx.Time + _duration;

            return status;
        }

        public override void Abort()
        {
            base.Abort();
            _readyAt = 0f;
        }
    }
}