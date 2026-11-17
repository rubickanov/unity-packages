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
        /// Scores a single item. Must return a value in the inclusive range [0, 1].
        /// Return any negative value to discard the item entirely (filter) — this holds
        /// regardless of <see cref="ScoreMode"/>, so a plain Score-mode test can also filter.
        /// Returning a value above 1 breaks score normalization and domination pruning;
        /// clamp your output if it can exceed the range.
        /// </summary>
        public abstract float Score(EQSQueryContext context, in EQSItem item);

        /// <summary>
        /// True if this test can never filter an item (never returns a negative score).
        /// The query engine only applies domination pruning across a span of tests when every
        /// remaining test reports <c>true</c> here — otherwise a later test could filter the
        /// current leader and a prematurely pruned item would have been the real winner.
        /// Default is <c>false</c> (conservative: assume the test may filter).
        /// </summary>
        public virtual bool NeverFilters => false;

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
        private static readonly HashSet<Type> _warnedRangeTypes = new();

        private void WarnFallbackOnce()
        {
            var type = GetType();
            if (_warnedTypes.Add(type))
                Debug.LogWarning(
                    $"{type.Name}: PreferBatch=true but ScoreBatch not overridden — falling back to per-item Score().");
        }

        internal void WarnOutOfRangeOnce()
        {
            var type = GetType();
            if (_warnedRangeTypes.Add(type))
                Debug.LogWarning(
                    $"{type.Name}: Score()/ScoreBatch() returned a value above 1. Scores must be in [0,1]; " +
                    "out-of-range values break score normalization and domination pruning.");
        }
#endif
    }
}
