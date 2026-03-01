using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Generates a grid of points around the querier.
    /// </summary>
    [Serializable]
    public class GridGenerator : EQSGenerator
    {
        [SerializeField] private float _halfExtent = 10f;
        [SerializeField] private float _spacing = 2f;

        public override void Generate(EQSQueryContext context, List<EQSItem> results)
        {
            Vector3 center = context.QuerierPosition;

            for (float x = -_halfExtent; x <= _halfExtent; x += _spacing)
            for (float z = -_halfExtent; z <= _halfExtent; z += _spacing)
            {
                results.Add(new EQSItem(center + new Vector3(x, 0f, z)));
            }
        }
    }
}
