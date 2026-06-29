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

        private Collider[]? _buffer;

        public override void Generate(EQSQueryContext context, List<EQSItem> results)
        {
            if (_buffer == null || _buffer.Length < _maxResults)
                _buffer = new Collider[_maxResults];

            int count = Physics.OverlapSphereNonAlloc(
                context.QuerierPosition, _radius, _buffer, _layerMask);

            int limit = Mathf.Min(count, _buffer.Length);

            for (int i = 0; i < limit; i++)
            {
                var col = _buffer[i];
                if (col.gameObject == context.QuerierObject) continue;
                results.Add(new EQSItem(col.transform.position, col.gameObject));
            }

            Array.Clear(_buffer, 0, count);
        }
    }
}
