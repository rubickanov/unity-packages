using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Pool;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Generates points evenly distributed on a circle.
    /// Can generate around the querier or around a reference position.
    /// </summary>
    [Serializable]
    public class CircleGenerator : EQSGenerator
    {
        [SerializeField] private float _radius = 8f;
        [SerializeField] private int _pointCount = 8;
        [SerializeField] private bool _aroundReference;
        [SerializeField] private bool _projectToGround;
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField] private float _raycastHeight = 50f;

        public override void Generate(EQSQueryContext context, List<EQSItem> results)
        {
            Vector3 center = _aroundReference && context.ReferencePosition.HasValue
                ? context.ReferencePosition.Value
                : context.QuerierPosition;

            if (_pointCount <= 0) return;

            var points = ListPool<Vector3>.Get();
            try
            {
                float angleStep = 360f / _pointCount;

                for (int i = 0; i < _pointCount; i++)
                {
                    float angle = angleStep * i * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _radius;
                    points.Add(center + offset);
                }

                if (!_projectToGround)
                {
                    for (int i = 0; i < points.Count; i++)
                        results.Add(new EQSItem(points[i]));
                    return;
                }

                int n = points.Count;

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
