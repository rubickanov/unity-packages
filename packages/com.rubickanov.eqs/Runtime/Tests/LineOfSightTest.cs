using System;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Binary test: 1 if clear line of sight from querier to item, 0 or filtered if blocked.
    /// </summary>
    [Serializable]
    public class LineOfSightTest : EQSTest
    {
        [SerializeField] private float _eyeHeight = 1.2f;
        [SerializeField] private float _targetHeight;
        [SerializeField] private LayerMask _obstacleMask = ~0;
        [SerializeField] private bool _filterOnFail = true;

        public override float Score(EQSQueryContext context, in EQSItem item)
        {
            Vector3 origin = context.QuerierPosition + Vector3.up * _eyeHeight;
            Vector3 target = item.Position + Vector3.up * _targetHeight;
            Vector3 dir = target - origin;
            float dist = dir.magnitude;

            if (dist < 0.01f) return 1f;

            bool blocked = Physics.Raycast(origin, dir.normalized, dist, _obstacleMask, QueryTriggerInteraction.Ignore);
            if (blocked) return _filterOnFail ? -1f : 0f;
            return 1f;
        }
    }
}
