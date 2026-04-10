using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.EQS.Tests
{
    [TestFixture]
    public class ScoreBatchFallbackTests
    {
        private CapturingLogHandler _capturer = null!;
        private ILogHandler _originalHandler = null!;

        [SetUp]
        public void SetUp()
        {
            // The warned-types set is static on EQSTest, so it persists across tests.
            // Clear it before each run so warning counts are deterministic regardless
            // of which order the test runner picks.
            ClearWarnedTypes();
            _capturer = new CapturingLogHandler();
            _originalHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = _capturer;
        }

        [TearDown]
        public void TearDown()
        {
            Debug.unityLogger.logHandler = _originalHandler;
            ClearWarnedTypes();
        }

        [Test]
        public void ScoreBatch_PreferBatchTrueNoOverride_LogsWarningExactlyOnce()
        {
            var test = new LocalNoOverrideBatchTest();
            var items = new List<EQSItem>
            {
                new EQSItem(Vector3.zero),
                new EQSItem(Vector3.right),
            };
            var alive = new[] { true, true };
            var rawScores = new float[2];
            var ctx = TestHelpers.MakeContext(Vector3.zero);

            test.ScoreBatch(ctx, items, alive, rawScores, 0, items.Count);
            test.ScoreBatch(ctx, items, alive, rawScores, 0, items.Count);
            test.ScoreBatch(ctx, items, alive, rawScores, 0, items.Count);

            Assert.AreEqual(
                1, CountWarningsContaining(nameof(LocalNoOverrideBatchTest)),
                "fallback warning must fire exactly once per type");
        }

        [Test]
        public void ScoreBatch_PreferBatchTrueNoOverride_PopulatesScoresViaPerItemScore()
        {
            var test = new LocalNoOverrideBatchTest();
            var items = new List<EQSItem>
            {
                new EQSItem(Vector3.zero),
                new EQSItem(Vector3.right),
            };
            var alive = new[] { true, true };
            var rawScores = new float[2];
            var ctx = TestHelpers.MakeContext(Vector3.zero);

            test.ScoreBatch(ctx, items, alive, rawScores, 0, items.Count);

            Assert.AreEqual(LocalNoOverrideBatchTest.FixedScore, rawScores[0]);
            Assert.AreEqual(LocalNoOverrideBatchTest.FixedScore, rawScores[1]);
        }

        [Test]
        public void ScoreBatch_PreferBatchFalse_DoesNotWarn()
        {
            var test = new LocalPerItemTest();
            var items = new List<EQSItem> { new EQSItem(Vector3.zero) };
            var alive = new[] { true };
            var rawScores = new float[1];
            var ctx = TestHelpers.MakeContext(Vector3.zero);

            test.ScoreBatch(ctx, items, alive, rawScores, 0, 1);
            test.ScoreBatch(ctx, items, alive, rawScores, 0, 1);

            Assert.AreEqual(0, CountWarningsContaining(nameof(LocalPerItemTest)));
        }

        [Test]
        public void ScoreBatch_DeadItemsInRange_AreSkippedByFallback()
        {
            var test = new LocalNoOverrideBatchTest();
            var items = new List<EQSItem>
            {
                new EQSItem(Vector3.zero),
                new EQSItem(Vector3.right),
                new EQSItem(Vector3.up),
            };
            var alive = new[] { true, false, true };
            var rawScores = new float[3];
            var ctx = TestHelpers.MakeContext(Vector3.zero);

            test.ScoreBatch(ctx, items, alive, rawScores, 0, 3);

            Assert.AreEqual(LocalNoOverrideBatchTest.FixedScore, rawScores[0]);
            Assert.AreEqual(0f, rawScores[1], "dead item must be skipped by fallback");
            Assert.AreEqual(LocalNoOverrideBatchTest.FixedScore, rawScores[2]);
        }

        [Test]
        public void ScoreBatch_RangeSubset_OnlyTouchesItemsInRange()
        {
            var test = new LocalNoOverrideBatchTest();
            var items = new List<EQSItem>
            {
                new EQSItem(Vector3.zero),
                new EQSItem(Vector3.right),
                new EQSItem(Vector3.up),
                new EQSItem(Vector3.forward),
            };
            var alive = new[] { true, true, true, true };
            var rawScores = new float[4];
            var ctx = TestHelpers.MakeContext(Vector3.zero);

            test.ScoreBatch(ctx, items, alive, rawScores, 1, 3);

            Assert.AreEqual(0f, rawScores[0], "index < startIndex must be untouched");
            Assert.AreEqual(LocalNoOverrideBatchTest.FixedScore, rawScores[1]);
            Assert.AreEqual(LocalNoOverrideBatchTest.FixedScore, rawScores[2]);
            Assert.AreEqual(0f, rawScores[3], "index >= endExclusive must be untouched");
        }

        private int CountWarningsContaining(string substr)
        {
            int n = 0;
            foreach (var (type, message) in _capturer.Captured)
                if (type == LogType.Warning && message.Contains(substr)) n++;
            return n;
        }

        // Reflection because _warnedTypes is a private static field guarded by
        // #if UNITY_EDITOR || DEVELOPMENT_BUILD. The field exists in the Editor
        // test runner; if it ever gets compiled out, the helper becomes a no-op.
        private static void ClearWarnedTypes()
        {
            var field = typeof(EQSTest).GetField(
                "_warnedTypes", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) return;

            var set = field.GetValue(null);
            if (set == null) return;

            var clearMethod = set.GetType().GetMethod("Clear");
            clearMethod?.Invoke(set, null);
        }

        // Local subclasses kept private so other tests can't accidentally pollute
        // the warned-types set with the same types.
        private sealed class LocalNoOverrideBatchTest : EQSTest
        {
            public const float FixedScore = 0.42f;
            public override bool PreferBatch => true;
            public override float Score(EQSQueryContext context, in EQSItem item) => FixedScore;
        }

        private sealed class LocalPerItemTest : EQSTest
        {
            public override bool PreferBatch => false;
            public override float Score(EQSQueryContext context, in EQSItem item) => 0.5f;
        }
    }
}
