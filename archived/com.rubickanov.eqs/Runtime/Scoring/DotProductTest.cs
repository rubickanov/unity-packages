using System;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Scores by dot product between the querier's forward direction and direction to item.
    /// Items in front score higher (1.0), items behind score lower (0.0).
    /// </summary>
    [Serializable]
    public class DotProductTest : EQSTest
    {
        // Output is always (dot + 1) * 0.5 ∈ [0, 1] — this test never filters.
        public override bool NeverFilters => true;

        public override float Score(EQSQueryContext context, in EQSItem item)
        {
            Vector3 toItem = item.Position - context.QuerierPosition;
            if (toItem.sqrMagnitude < 0.001f) return 0.5f;
            float dot = Vector3.Dot(context.QuerierForward, toItem.normalized);
            return (dot + 1f) * 0.5f;
        }
    }
}
