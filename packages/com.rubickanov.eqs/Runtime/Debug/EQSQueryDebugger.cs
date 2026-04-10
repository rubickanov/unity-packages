using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.EQS
{
    public class EQSQueryDebugger : MonoBehaviour
    {
        [SerializeField] private EQSQueryConfig? _queryConfig;
        [SerializeField] private Transform? _referenceTransform;
        [SerializeField] private bool _autoRefresh;
        [SerializeField] private float _itemSphereRadius = 0.3f;

        private readonly List<EQSItem> _generatedItems = new();
        private EQSQueryResult _lastResult;
        private EQSQuery? _query;

        public int GeneratedCount => _generatedItems.Count;
        public EQSQueryResult LastResult => _lastResult;

        private void Update()
        {
            if (_autoRefresh)
                RunQuery();
        }

        public void RunQuery()
        {
            if (_queryConfig == null) return;

            _query ??= new EQSQuery(_queryConfig);

            Vector3? refPos = _referenceTransform != null
                ? _referenceTransform.position
                : null;

            var context = new EQSQueryContext(
                transform.position,
                transform.forward,
                gameObject,
                refPos);

            _lastResult = _query.RunSync(context);

            _generatedItems.Clear();
            var items = _query.Items;
            for (int i = 0; i < items.Count; i++)
                _generatedItems.Add(items[i]);
        }

        private void OnDrawGizmosSelected()
        {
            float r = _itemSphereRadius;

            // Generated items: small gray spheres
            Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            for (int i = 0; i < _generatedItems.Count; i++)
                Gizmos.DrawSphere(_generatedItems[i].Position, r * 0.5f);

            if (_lastResult.Items == null || _lastResult.Items.Count == 0) return;

            int count = _lastResult.Items.Count;

            // Scored items: green(best) → red(worst) gradient
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0f;
                Gizmos.color = Color.Lerp(Color.green, Color.red, t);
                Gizmos.DrawSphere(_lastResult.Items[i].Position, r);
            }

            // Best item: blue sphere
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(_lastResult.Items[0].Position, r * 1.5f);
        }
    }
}
