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
        [SerializeField] private bool _projectToGround;
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField] private float _raycastHeight = 50f;

        public override void Generate(EQSQueryContext context, List<EQSItem> results)
        {
            Vector3 center = context.QuerierPosition;

            for (float x = -_halfExtent; x <= _halfExtent; x += _spacing)
            for (float z = -_halfExtent; z <= _halfExtent; z += _spacing)
            {
                Vector3 point = center + new Vector3(x, 0f, z);

                if (_projectToGround)
                {
                    var ray = new Ray(point + Vector3.up * _raycastHeight, Vector3.down);
                    if (!Physics.Raycast(ray, out var hit, _raycastHeight * 2f, _groundMask))
                        continue;
                    point = hit.point;
                }

                results.Add(new EQSItem(point));
            }
        }
    }
}
