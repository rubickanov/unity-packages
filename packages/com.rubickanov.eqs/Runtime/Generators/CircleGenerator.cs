using System;
using System.Collections.Generic;
using UnityEngine;

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

        public override void Generate(EQSQueryContext context, List<EQSItem> results)
        {
            Vector3 center = _aroundReference && context.ReferencePosition.HasValue
                ? context.ReferencePosition.Value
                : context.QuerierPosition;

            float angleStep = 360f / _pointCount;

            for (int i = 0; i < _pointCount; i++)
            {
                float angle = angleStep * i * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _radius;
                results.Add(new EQSItem(center + offset));
            }
        }
    }
}
