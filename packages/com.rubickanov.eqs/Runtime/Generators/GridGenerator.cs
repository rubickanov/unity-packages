using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Pool;

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

            var points = ListPool<Vector3>.Get();
            try
            {
                // Integer step counting — a float accumulator (x += _spacing) drifts and can
                // drop the last row/column when _halfExtent isn't an exact multiple of _spacing.
                int steps = _spacing > 0f ? Mathf.FloorToInt(_halfExtent / _spacing) : 0;
                for (int ix = -steps; ix <= steps; ix++)
                for (int iz = -steps; iz <= steps; iz++)
                    points.Add(center + new Vector3(ix * _spacing, 0f, iz * _spacing));

                if (!_projectToGround)
                {
                    for (int i = 0; i < points.Count; i++)
                        results.Add(new EQSItem(points[i]));
                    return;
                }

                int n = points.Count;
                if (n == 0) return;

                // Not `using var` because `using` locals are readonly, which blocks
                // NativeArray's indexer setter. Explicit try/finally handles disposal.
                var commands = new NativeArray<RaycastCommand>(n, Allocator.TempJob);
                var hits = new NativeArray<RaycastHit>(n, Allocator.TempJob);
                try
                {
                    var queryParams = new QueryParameters(
                        layerMask: _groundMask.value,
                        hitMultipleFaces: false,
                        hitTriggers: QueryTriggerInteraction.Ignore,
                        hitBackfaces: false);

                    float maxDistance = _raycastHeight * 2f;

                    for (int i = 0; i < n; i++)
                    {
                        Vector3 origin = points[i] + Vector3.up * _raycastHeight;
                        commands[i] = new RaycastCommand(origin, Vector3.down, queryParams, maxDistance);
                    }

                    RaycastCommand.ScheduleBatch(commands, hits, minCommandsPerJob: 16).Complete();

                    for (int i = 0; i < n; i++)
                    {
                        if (hits[i].collider != null)
                            results.Add(new EQSItem(hits[i].point));
                        // miss → skipped, matching pre-refactor behaviour
                    }
                }
                finally
                {
                    commands.Dispose();
                    hits.Dispose();
                }
            }
            finally
            {
                ListPool<Vector3>.Release(points);
            }
        }
    }
}
