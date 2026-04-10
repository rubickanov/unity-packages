using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Rubickanov.EQS.Tests
{
    /// <summary>
    /// Reflection-based builder for EQSQueryConfig. The production type only exposes
    /// inspector-driven configuration via [SerializeReference] private fields, so
    /// tests poke them directly. Same pattern used by ReplicationScannerTests.
    /// </summary>
    internal static class TestHelpers
    {
        private static readonly FieldInfo GeneratorField = typeof(EQSQueryConfig)
            .GetField("_generator", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo TestsField = typeof(EQSQueryConfig)
            .GetField("_tests", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public static EQSQueryConfig MakeConfig(EQSGenerator? generator, params EQSTest[] tests)
        {
            var config = ScriptableObject.CreateInstance<EQSQueryConfig>();
            // Set fields directly so OnValidate's null-warnings don't fire — they'd
            // pollute test logs even though they're informational only.
            GeneratorField.SetValue(config, generator);
            TestsField.SetValue(config, new List<EQSTest>(tests));
            return config;
        }

        public static EQSQueryContext MakeContext(Vector3 position) =>
            new EQSQueryContext(position, Vector3.forward);
    }

    /// <summary>
    /// Reflection-based setters for EQSTest's private SerializeField fields.
    /// In production these are inspector-driven; tests need to control them
    /// without going through serialized assets.
    /// </summary>
    internal static class TestExtensions
    {
        private static readonly FieldInfo WeightField = typeof(EQSTest)
            .GetField("_weight", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo ScoreModeField = typeof(EQSTest)
            .GetField("_scoreMode", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo NormalizeField = typeof(EQSTest)
            .GetField("_normalize", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public static T Configure<T>(
            this T test,
            float weight = 1f,
            EQSTestScoreMode mode = EQSTestScoreMode.Score,
            bool normalize = false) where T : EQSTest
        {
            WeightField.SetValue(test, weight);
            ScoreModeField.SetValue(test, mode);
            NormalizeField.SetValue(test, normalize);
            return test;
        }
    }

    /// <summary>
    /// Generator that returns a fixed list of points provided by the test.
    /// Lets us decouple test logic from spatial layouts.
    /// </summary>
    internal sealed class FixedPointsGenerator : EQSGenerator
    {
        private readonly Vector3[] _points;

        public FixedPointsGenerator(params Vector3[] points) => _points = points;

        public override void Generate(EQSQueryContext context, List<EQSItem> results)
        {
            for (int i = 0; i < _points.Length; i++)
                results.Add(new EQSItem(_points[i]));
        }
    }

    /// <summary>
    /// Test that returns a pre-supplied score per item, keyed on Position.x as
    /// the item index. Pair with FixedPointsGenerator placing points at
    /// (0,0,0), (1,0,0), ... to address tests by index.
    /// </summary>
    internal sealed class IndexedScoreTest : EQSTest
    {
        private readonly Dictionary<int, float> _scoresByIndex = new();

        public IndexedScoreTest(params float[] scores)
        {
            for (int i = 0; i < scores.Length; i++)
                _scoresByIndex[i] = scores[i];
        }

        public override float Score(EQSQueryContext context, in EQSItem item)
        {
            int idx = Mathf.RoundToInt(item.Position.x);
            return _scoresByIndex[idx];
        }
    }

    /// <summary>
    /// Test that scores items by their Z position (deterministic, no physics).
    /// Used for batch-vs-per-item parity tests where the same scoring logic must
    /// run through both code paths.
    /// </summary>
    internal sealed class DeterministicTest : EQSTest
    {
        public bool BatchPreferred;
        public int ChunkSize = 32;
        public int ScoreCallCount;
        public int ScoreBatchCallCount;

        public override bool PreferBatch => BatchPreferred;
        public override int BatchChunkSize => ChunkSize;

        public override float Score(EQSQueryContext context, in EQSItem item)
        {
            ScoreCallCount++;
            return Compute(item);
        }

        public override void ScoreBatch(
            EQSQueryContext context, IReadOnlyList<EQSItem> items,
            bool[] alive, float[] rawScores, int startIndex, int endExclusive)
        {
            ScoreBatchCallCount++;
            for (int i = startIndex; i < endExclusive; i++)
            {
                if (!alive[i]) continue;
                rawScores[i] = Compute(items[i]);
            }
        }

        private static float Compute(in EQSItem item) =>
            Mathf.Clamp01(item.Position.z * 0.1f + 0.05f);
    }

    /// <summary>
    /// Spy that records how many times it was called. Used to verify that
    /// domination pruning skips items in subsequent tests.
    /// </summary>
    internal sealed class SpyTest : EQSTest
    {
        public int ScoreCalls;

        public override float Score(EQSQueryContext context, in EQSItem item)
        {
            ScoreCalls++;
            return 0.5f;
        }
    }

    /// <summary>
    /// Test with PreferBatch=true but no ScoreBatch override. Triggers the
    /// fallback warn-once path in the base class.
    /// </summary>
    internal sealed class NoOverrideBatchTest : EQSTest
    {
        public override bool PreferBatch => true;

        public override float Score(EQSQueryContext context, in EQSItem item) => 0.5f;
    }

    /// <summary>
    /// Captures Debug log calls without forwarding them to Unity's console — used
    /// when a test deliberately triggers a warning and we don't want it polluting
    /// the test runner output. Same approach as ReplicationScannerTests.
    /// </summary>
    internal sealed class CapturingLogHandler : ILogHandler
    {
        public readonly List<(LogType type, string message)> Captured = new();

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            Captured.Add((logType, string.Format(format, args)));
        }

        public void LogException(System.Exception exception, Object context)
        {
            Captured.Add((LogType.Exception, exception.Message));
        }
    }
}
