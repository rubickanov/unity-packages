using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Finds GameObjects in radius via Physics.OverlapSphereNonAlloc.
    /// </summary>
    [Serializable]
    public class SphereOverlapGenerator : EQSGenerator
    {
        [SerializeField] private float _radius = 15f;
        [SerializeField] private LayerMask _layerMask = ~0;
        [SerializeField] private int _maxResults = 32;

        private static readonly Collider[] Buffer = new Collider[64];

        public override void Generate(EQSQueryContext context, List<EQSItem> results)
        {
            int count = Physics.OverlapSphereNonAlloc(
                context.QuerierPosition, _radius, Buffer, _layerMask);

            int limit = Mathf.Min(count, Mathf.Min(_maxResults, Buffer.Length));

            for (int i = 0; i < limit; i++)
            {
                var col = Buffer[i];
                if (col.gameObject == context.QuerierObject) continue;
                results.Add(new EQSItem(col.transform.position, col.gameObject));
            }
        }
    }
}
