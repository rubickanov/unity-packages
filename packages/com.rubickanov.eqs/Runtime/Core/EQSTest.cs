using System;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Scores a single item on a 0..1 scale.
    /// Return a negative value to filter out the item entirely.
    /// </summary>
    [Serializable]
    public abstract class EQSTest
    {
        [SerializeField] private float _weight = 1f;
        [SerializeField] private EQSTestScoreMode _scoreMode = EQSTestScoreMode.Score;

        public float Weight => _weight;
        public EQSTestScoreMode ScoreMode => _scoreMode;

        /// <summary>
        /// Scores a single item. Must return a value in [0, 1].
        /// Return a negative value to discard the item (filter).
        /// </summary>
        public abstract float Score(EQSQueryContext context, in EQSItem item);
    }
}
