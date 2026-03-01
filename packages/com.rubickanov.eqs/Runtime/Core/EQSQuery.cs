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
        private int _currentTestIndex;
        private int _currentItemIndex;

        private List<EQSScoredItem>? _resultItems;
        private EQSQueryResult _result;

        private static readonly Stopwatch Stopwatch = new();

        public EQSQueryStatus Status => _status;

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

            Stopwatch.Restart();

            var tests = _config.Tests;
            int itemCount = _items.Count;

            while (_currentTestIndex < tests.Count)
            {
                var test = tests[_currentTestIndex];
                float weight = test.Weight;
                bool inverse = test.ScoreMode == EQSTestScoreMode.InverseScore;

                while (_currentItemIndex < itemCount)
                {
                    if (Stopwatch.Elapsed.TotalMilliseconds > budgetMs)
                    {
                        Stopwatch.Stop();
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
                    }
                    else
                    {
                        if (inverse) raw = 1f - raw;
                        _scores![_currentItemIndex] += raw * weight;
                    }

                    _currentItemIndex++;
                }

                _currentItemIndex = 0;
                _currentTestIndex++;
            }

            Stopwatch.Stop();

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

            _result = new EQSQueryResult(_resultItems.Count > 0, _resultItems);
        }
    }
}
