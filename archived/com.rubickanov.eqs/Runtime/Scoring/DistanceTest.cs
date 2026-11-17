using System;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Scores by distance from the querier to the item.
    /// Closer items score higher. Items beyond max distance are filtered out.
    /// </summary>
    [Serializable]
    public class DistanceTest : EQSTest
    {
        [SerializeField] private float _maxDistance = 20f;

        public override float Score(EQSQueryContext context, in EQSItem item)
        {
            // Filter on squared distance to skip the sqrt for out-of-range items.
            float sqrDist = (item.Position - context.QuerierPosition).sqrMagnitude;
            if (sqrDist > _maxDistance * _maxDistance) return -1f;

            float dist = Mathf.Sqrt(sqrDist);
            return 1f - Mathf.Clamp01(dist / _maxDistance);
        }
    }
}
