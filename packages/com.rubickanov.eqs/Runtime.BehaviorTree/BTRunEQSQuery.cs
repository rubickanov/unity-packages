using System;
using Rubickanov.BehaviorTree.Runtime;
using UnityEngine;

namespace Rubickanov.EQS
{
    [Serializable]
    [BTNodeDescription("Run EQS Query", "EQS", "Runs an EQS query and stores the best position in the blackboard.")]
    public class BTRunEQSQuery : BTLeafAction
    {
        [SerializeField] private EQSQueryConfig _queryConfig = default!;
        [SerializeField] private float _budgetMs = 0.5f;

        [NonSerialized] private EQSQuery? _query;
        [NonSerialized] private Transform? _transform;

        protected override BTStatus OnExecute(BTContext ctx)
        {
            _transform ??= ((Component)ctx.Owner!).transform;

            if (_query == null || _query.Status is EQSQueryStatus.Complete or EQSQueryStatus.Failed or EQSQueryStatus.NotStarted)
            {
                _query ??= new EQSQuery(_queryConfig);

                Vector3? referencePos = null;
                if (ctx.Blackboard.TryGet(EQSBlackboardKeys.ReferencePosition, out Vector3 refPos))
                    referencePos = refPos;

                var context = new EQSQueryContext(
                    _transform.position,
                    _transform.forward,
                    _transform.gameObject,
                    referencePos);

                _query.Start(context);
            }

            if (_query.Tick(_budgetMs))
            {
                var result = _query.GetResult();
                if (result.Success && result.TryGetBest(out var best))
                {
                    ctx.Blackboard.Set(EQSBlackboardKeys.BestPosition, best.Position);
                    return BTStatus.Success;
                }

                return BTStatus.Failure;
            }

            return BTStatus.Running;
        }

        public override void Abort()
        {
            base.Abort();
            _query?.Reset();
            _transform = null;
        }
    }
}
