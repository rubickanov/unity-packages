using System;
using System.Collections.Generic;
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
        [SerializeField] private bool _normalize;

        public float Weight => _weight;
        public EQSTestScoreMode ScoreMode => _scoreMode;
        public bool Normalize => _normalize;

        /// <summary>
        /// Scores a single item. Must return a value in [0, 1].
        /// Return a negative value to discard the item (filter).
        /// </summary>
        public abstract float Score(EQSQueryContext context, in EQSItem item);

        /// <summary>
        /// When true, the query engine will call <see cref="ScoreBatch"/> instead of per-item <see cref="Score"/>.
        /// </summary>
        public virtual bool PreferBatch => false;

        /// <summary>
        /// Scores all alive items in one call. Override for vectorized or Physics batch operations.
        /// Default implementation loops over <see cref="Score"/>.
        /// </summary>
        public virtual void ScoreBatch(
            EQSQueryContext context, IReadOnlyList<EQSItem> items,
            bool[] alive, float[] rawScores, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!alive[i]) continue;
                rawScores[i] = Score(context, in items[i]);
            }
        }
    }
}
