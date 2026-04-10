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
        /// Items per chunk when the engine runs <see cref="ScoreBatch"/>.
        /// The engine checks the frame budget between chunks, so smaller values
        /// give finer-grained budget control at the cost of more job overhead.
        /// </summary>
        public virtual int BatchChunkSize => 32;

        /// <summary>
        /// Scores items in the half-open range [<paramref name="startIndex"/>, <paramref name="endExclusive"/>)
        /// in one call. Override for vectorized or Physics batch operations (e.g. RaycastCommand).
        /// Default implementation loops over <see cref="Score"/>.
        /// </summary>
        public virtual void ScoreBatch(
            EQSQueryContext context, IReadOnlyList<EQSItem> items,
            bool[] alive, float[] rawScores, int startIndex, int endExclusive)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PreferBatch) WarnFallbackOnce();
#endif
            for (int i = startIndex; i < endExclusive; i++)
            {
                if (!alive[i]) continue;
                rawScores[i] = Score(context, items[i]);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly HashSet<Type> _warnedTypes = new();

        private void WarnFallbackOnce()
        {
            var type = GetType();
            if (_warnedTypes.Add(type))
                Debug.LogWarning(
                    $"{type.Name}: PreferBatch=true but ScoreBatch not overridden — falling back to per-item Score().");
        }
#endif
    }
}
