using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Serializable query configuration. Contains one generator and a list of weighted tests.
    /// </summary>
    [CreateAssetMenu(fileName = "EQSQuery", menuName = "EQS/Query Config")]
    public class EQSQueryConfig : ScriptableObject
    {
        [SerializeReference] private EQSGenerator? _generator;
        [SerializeReference] private List<EQSTest> _tests = new();

        public EQSGenerator? Generator => _generator;
        public IReadOnlyList<EQSTest> Tests => _tests;

        private void OnValidate()
        {
            if (_generator == null)
                Debug.LogWarning($"[EQS] {name}: Generator is not assigned.", this);
            if (_tests.Count == 0)
                Debug.LogWarning($"[EQS] {name}: No tests configured.", this);
        }
    }
}
