using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Executes an EQS query with optional time budgeting across frames.
    /// </summary>
    public class EQSQuery
    {
        private readonly EQSQueryConfig _config;

        private EQSQueryContext _context;
        private EQSQueryStatus _status = EQSQueryStatus.NotStarted;

        private readonly List<EQSItem> _items = new();
        private float[]? _scores;
        private bool[]? _alive;
        private float[]? _rawTestScores;
        private int _currentTestIndex;
        private int _currentItemIndex;

        private List<EQSScoredItem>? _resultItems;
        private EQSQueryResult _result;

        private readonly Stopwatch _stopwatch = new();

        public EQSQueryStatus Status => _status;

        /// <summary>
        /// The items produced by the generator. Read-only; valid after <see cref="Start"/>.
        /// </summary>
        public IReadOnlyList<EQSItem> Items => _items;

        public EQSQuery(EQSQueryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Starts query execution. Call <see cref="Tick"/> each frame to advance.
        /// </summary>
        public void Start(EQSQueryContext context)
        {
            _context = context;
            _items.Clear();
            _currentTestIndex = 0;
            _currentItemIndex = 0;

            if (_config.Generator == null)
            {
                _status = EQSQueryStatus.Failed;
                _result = new EQSQueryResult(false, Array.Empty<EQSScoredItem>());
                return;
            }

            _status = EQSQueryStatus.Generating;
            _config.Generator.Generate(_context, _items);

            if (_items.Count == 0)
            {
                _status = EQSQueryStatus.Failed;
                _result = new EQSQueryResult(false, Array.Empty<EQSScoredItem>());
                return;
            }

            int count = _items.Count;

            if (_scores == null || _scores.Length < count)
                _scores = new float[count];
            else
                Array.Clear(_scores, 0, count);

            if (_alive == null || _alive.Length < count)
                _alive = new bool[count];

            if (_rawTestScores == null || _rawTestScores.Length < count)
                _rawTestScores = new float[count];

            for (int i = 0; i < count; i++)
                _alive[i] = true;

            _status = _config.Tests.Count > 0
                ? EQSQueryStatus.Scoring
                : EQSQueryStatus.Complete;

            if (_status == EQSQueryStatus.Complete)
                BuildResult();
        }

        /// <summary>
        /// Advances the query by up to <paramref name="budgetMs"/> milliseconds.
        /// Returns true when the query is complete (Status == Complete or Failed).
        /// </summary>
        public bool Tick(float budgetMs = float.MaxValue)
        {
            if (_status != EQSQueryStatus.Scoring)
                return _status is EQSQueryStatus.Complete or EQSQueryStatus.Failed;

            _stopwatch.Restart();

            var tests = _config.Tests;
            int itemCount = _items.Count;

            while (_currentTestIndex < tests.Count)
            {
                var test = tests[_currentTestIndex];

                if (test == null)
                {
                    _currentItemIndex = 0;
                    _currentTestIndex++;
                    continue;
                }

                bool filterOnly = test.ScoreMode == EQSTestScoreMode.FilterOnly;
                bool normalize = test.Normalize && !filterOnly;
                float weight = test.Weight;
                bool inverse = test.ScoreMode == EQSTestScoreMode.InverseScore;

                // First pass: score all items for this test
                if (test.PreferBatch)
                {
                    // Batch path — chunked so the budget is honoured between chunks.
                    int chunkSize = Math.Max(1, test.BatchChunkSize);

                    while (_currentItemIndex < itemCount)
                    {
                        if (_stopwatch.Elapsed.TotalMilliseconds > budgetMs)
                        {
                            _stopwatch.Stop();
                            return false;
                        }

                        int end = Math.Min(_currentItemIndex + chunkSize, itemCount);
                        test.ScoreBatch(_context, _items, _alive!, _rawTestScores!, _currentItemIndex, end);

                        for (int i = _currentItemIndex; i < end; i++)
                        {
                            if (!_alive![i]) continue;
                            if (_rawTestScores![i] < 0f)
                            {
                                _alive[i] = false;
                                _rawTestScores[i] = -1f;
                            }
                        }

                        _currentItemIndex = end;
                    }
                }
                else
                {
                    // Per-item path with budget checking
                    while (_currentItemIndex < itemCount)
                    {
                        if (_stopwatch.Elapsed.TotalMilliseconds > budgetMs)
                        {
                            _stopwatch.Stop();
                            return false;
                        }

                        if (!_alive![_currentItemIndex])
                        {
                            _currentItemIndex++;
                            continue;
                        }

                        var item = _items[_currentItemIndex];
                        float raw = test.Score(_context, in item);

                        if (raw < 0f)
                        {
                            _alive[_currentItemIndex] = false;
                            _rawTestScores![_currentItemIndex] = -1f;
                        }
                        else
                        {
                            _rawTestScores![_currentItemIndex] = raw;
                        }

                        _currentItemIndex++;
                    }
                }

                // Test complete — normalize if needed and accumulate scores
                if (!filterOnly)
                {
                    float min = float.MaxValue;
                    float max = float.MinValue;

                    if (normalize)
                    {
                        for (int i = 0; i < itemCount; i++)
                        {
                            if (!_alive![i]) continue;
                            float v = _rawTestScores![i];
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                    }

                    float range = max - min;

                    for (int i = 0; i < itemCount; i++)
                    {
                        if (!_alive![i]) continue;

                        float score = _rawTestScores![i];

                        if (normalize && range > 0f)
                            score = (score - min) / range;

                        if (inverse) score = 1f - score;
                        _scores![i] += score * weight;
                    }
                }

                // Early exit by domination: prune items that can't possibly win
                float remainingWeight = 0f;
                for (int t = _currentTestIndex + 1; t < tests.Count; t++)
                {
                    var ft = tests[t];
                    if (ft == null || ft.ScoreMode == EQSTestScoreMode.FilterOnly) continue;
                    remainingWeight += ft.Weight;
                }

                if (remainingWeight > 0f)
                {
                    float bestScore = float.MinValue;
                    for (int i = 0; i < itemCount; i++)
                        if (_alive![i] && _scores![i] > bestScore) bestScore = _scores[i];

                    if (bestScore > float.MinValue)
                        for (int i = 0; i < itemCount; i++)
                            if (_alive![i] && _scores![i] + remainingWeight < bestScore)
                                _alive[i] = false;
                }

                _currentItemIndex = 0;
                _currentTestIndex++;
            }

            _stopwatch.Stop();

            _status = EQSQueryStatus.Complete;
            BuildResult();
            return true;
        }

        /// <summary>
        /// Runs the entire query synchronously in one call.
        /// </summary>
        public EQSQueryResult RunSync(EQSQueryContext context)
        {
            Start(context);

            if (_status == EQSQueryStatus.Scoring)
                Tick();

            return _result;
        }

        /// <summary>
        /// Gets the result. Only valid when Status is Complete or Failed.
        /// </summary>
        public EQSQueryResult GetResult() => _result;

        /// <summary>
        /// Resets the query so it can be reused with a new context.
        /// </summary>
        public void Reset()
        {
            _status = EQSQueryStatus.NotStarted;
            _items.Clear();
            _currentTestIndex = 0;
            _currentItemIndex = 0;
            _result = default;
        }

        private void BuildResult()
        {
            _resultItems ??= new List<EQSScoredItem>();
            _resultItems.Clear();

            int count = _items.Count;
            for (int i = 0; i < count; i++)
            {
                if (!_alive![i]) continue;

                var item = _items[i];
                _resultItems.Add(new EQSScoredItem(item.Position, item.Object, _scores![i]));
            }

            _resultItems.Sort(static (a, b) => b.Score.CompareTo(a.Score));

            _result = new EQSQueryResult(_resultItems.Count > 0, _resultItems.ToArray());
        }
    }
}
