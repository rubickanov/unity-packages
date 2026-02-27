using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    [Serializable]
    [BTNodeDescription("Selector", "Composites", "Runs children in order. Succeeds on first success, fails when all fail.")]
    public class BTSelector : BTComposite
    {
        [NonSerialized] private int _runningIndex = -1;

        public BTSelector() { }
        public BTSelector(params BTNode[] children) : base(children) { }

        protected override BTStatus OnTick(BTContext ctx)
        {
            for (int i = 0; i < Children.Length; i++)
            {
                var status = Children[i].Tick(ctx);

                if (status == BTStatus.Running || status == BTStatus.Success)
                {
                    if (_runningIndex >= 0 && _runningIndex != i)
                        Children[_runningIndex].Abort();

                    _runningIndex = status == BTStatus.Running ? i : -1;
                    return status;
                }
            }

            _runningIndex = -1;
            return BTStatus.Failure;
        }

        public override void Abort()
        {
            _runningIndex = -1;
            base.Abort();
        }
    }
}