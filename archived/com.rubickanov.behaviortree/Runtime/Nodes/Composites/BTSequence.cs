using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    [Serializable]
    [BTNodeDescription("Sequence", "Composites", "Runs children in order. Fails on first failure, succeeds when all succeed.")]
    public class BTSequence : BTComposite
    {
        [NonSerialized] private int _currentIndex;

        public BTSequence() { }
        public BTSequence(params BTNode[] children) : base(children) { }

        protected override BTStatus OnTick(BTContext ctx)
        {
            for (int i = _currentIndex; i < Children.Length; i++)
            {
                var status = Children[i].Tick(ctx);

                if (status == BTStatus.Running)
                {
                    _currentIndex = i;
                    return BTStatus.Running;
                }

                if (status == BTStatus.Failure)
                {
                    _currentIndex = 0;
                    return BTStatus.Failure;
                }
            }

            _currentIndex = 0;
            return BTStatus.Success;
        }

        public override void Abort()
        {
            _currentIndex = 0;
            base.Abort();
        }
    }
}