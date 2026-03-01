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
            float dist = Vector3.Distance(context.QuerierPosition, item.Position);
            if (dist > _maxDistance) return -1f;
            return 1f - Mathf.Clamp01(dist / _maxDistance);
        }
    }
}
