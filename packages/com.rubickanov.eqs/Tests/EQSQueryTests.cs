using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.EQS.Tests
{
    [TestFixture]
    public class EQSQueryTests
    {
        // ---- Status / lifecycle -------------------------------------------------

        [Test]
        public void Start_NullGenerator_StatusFailed()
        {
            var config = TestHelpers.MakeConfig(generator: null);
            var query = new EQSQuery(config);

            query.Start(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(EQSQueryStatus.Failed, query.Status);
            Assert.IsFalse(query.GetResult().Success);
        }

        [Test]
        public void Start_GeneratorProducesZeroItems_StatusFailed()
        {
            var config = TestHelpers.MakeConfig(new FixedPointsGenerator());
            var query = new EQSQuery(config);

            query.Start(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(EQSQueryStatus.Failed, query.Status);
        }

        [Test]
        public void RunSync_NoTests_StatusComplete()
        {
            var config = TestHelpers.MakeConfig(
                new FixedPointsGenerator(Vector3.zero, Vector3.right));
            var query = new EQSQuery(config);

            var result = query.RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(EQSQueryStatus.Complete, query.Status);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.Items.Count);
        }

        // ---- Sorting & scoring --------------------------------------------------

        [Test]
        public void RunSync_SingleScoreTest_ItemsSortedDescendingByScore()
        {
            // Three items at x = 0,1,2 → IndexedScoreTest assigns scores 0.1, 0.5, 1.0.
            // Expected order in result: x=2 (1.0), x=1 (0.5), x=0 (0.1).
            var generator = new FixedPointsGenerator(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f));
            var test = new IndexedScoreTest(0.1f, 0.5f, 1.0f);
            var config = TestHelpers.MakeConfig(generator, test);
            var query = new EQSQuery(config);

            var result = query.RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.Items.Count);
            Assert.AreEqual(2f, result.Items[0].Position.x);
            Assert.AreEqual(1f, result.Items[1].Position.x);
            Assert.AreEqual(0f, result.Items[2].Position.x);
        }

        [Test]
        public void RunSync_NegativeScore_FiltersItem()
        {
            var generator = new FixedPointsGenerator(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f));
            // -1f filters the middle item.
            var test = new IndexedScoreTest(0.5f, -1f, 0.8f);
            var config = TestHelpers.MakeConfig(generator, test);
            var query = new EQSQuery(config);

            var result = query.RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(2, result.Items.Count);
            // x=1 should be missing entirely.
            foreach (var item in result.Items)
                Assert.AreNotEqual(1f, item.Position.x);
        }

        // ---- Batch vs per-item parity ------------------------------------------

        [Test]
        public void Tick_BatchAndPerItemPaths_ProduceIdenticalResults()
        {
            // Identical query setup, only the test's PreferBatch flag changes.
            // Final scores must match exactly because the underlying scoring logic
            // is identical between Score() and ScoreBatch().
            var points = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 0f, 3f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 0f, 7f),
                new Vector3(0f, 0f, 9f),
            };

            var perItemTest = new DeterministicTest { BatchPreferred = false };
            var perItemConfig = TestHelpers.MakeConfig(new FixedPointsGenerator(points), perItemTest);
            var perItemResult = new EQSQuery(perItemConfig).RunSync(TestHelpers.MakeContext(Vector3.zero));

            var batchTest = new DeterministicTest { BatchPreferred = true };
            var batchConfig = TestHelpers.MakeConfig(new FixedPointsGenerator(points), batchTest);
            var batchResult = new EQSQuery(batchConfig).RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(perItemResult.Items.Count, batchResult.Items.Count);
            for (int i = 0; i < perItemResult.Items.Count; i++)
            {
                Assert.AreEqual(
                    perItemResult.Items[i].Position, batchResult.Items[i].Position,
                    $"Item {i} position differs between paths");
                Assert.AreEqual(
                    perItemResult.Items[i].Score, batchResult.Items[i].Score, 1e-5f,
                    $"Item {i} score differs between paths");
            }

            Assert.Greater(perItemTest.ScoreCallCount, 0, "per-item path must call Score()");
            Assert.AreEqual(0, batchTest.ScoreCallCount, "batch path must not call Score()");
            Assert.Greater(batchTest.ScoreBatchCallCount, 0, "batch path must call ScoreBatch()");
        }

        [Test]
        public void Tick_BatchChunkSize_ControlsScoreBatchInvocationCount()
        {
            var points = new Vector3[10];
            for (int i = 0; i < 10; i++) points[i] = new Vector3(0f, 0f, i);

            var test = new DeterministicTest { BatchPreferred = true, ChunkSize = 1 };
            var config = TestHelpers.MakeConfig(new FixedPointsGenerator(points), test);

            new EQSQuery(config).RunSync(TestHelpers.MakeContext(Vector3.zero));

            // ChunkSize=1 → one ScoreBatch call per item.
            Assert.AreEqual(10, test.ScoreBatchCallCount);
        }

        [Test]
        public void Tick_BatchChunkSize_LargerThanItemCount_RunsSingleChunk()
        {
            var points = new Vector3[3];
            for (int i = 0; i < 3; i++) points[i] = new Vector3(0f, 0f, i);

            var test = new DeterministicTest { BatchPreferred = true, ChunkSize = 100 };
            var config = TestHelpers.MakeConfig(new FixedPointsGenerator(points), test);

            new EQSQuery(config).RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(1, test.ScoreBatchCallCount);
        }

        // ---- Domination pruning -------------------------------------------------

        [Test]
        public void Tick_DominationPruning_SkipsHopelessItemsInLaterTests()
        {
            // Three items, two tests. After test 1, scores are [1.0, 0.5, 0.1].
            // Test 2 has weight 0.5, so remainingWeight after test 1 is 0.5.
            // bestScore = 1.0. Items survive iff score + 0.5 >= 1.0, i.e. score >= 0.5.
            // Item with score 0.1 must be pruned BEFORE test 2 runs → SpyTest sees only 2.
            var generator = new FixedPointsGenerator(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f));
            var firstTest = new IndexedScoreTest(0.1f, 0.5f, 1.0f).Configure(weight: 1f);
            var spy = new SpyTest().Configure(weight: 0.5f);
            var config = TestHelpers.MakeConfig(generator, firstTest, spy);
            var query = new EQSQuery(config);

            query.RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(2, spy.ScoreCalls,
                "domination pruning should skip the item with score 0.1");
        }

        [Test]
        public void Tick_DominationPruning_AppliesToBatchPath()
        {
            // Same setup as the per-item test, but the second test uses the batch
            // path. Verifies the dominated item is missing from the final result —
            // we assert via the result, not via ScoreBatch call counts, because
            // chunk-loop iteration over dead items is an implementation detail.
            var generator = new FixedPointsGenerator(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f));
            var firstTest = new IndexedScoreTest(0.1f, 0.5f, 1.0f).Configure(weight: 1f);
            var batchTest = new DeterministicTest { BatchPreferred = true, ChunkSize = 1 };
            batchTest.Configure(weight: 0.5f);
            var config = TestHelpers.MakeConfig(generator, firstTest, batchTest);

            var result = new EQSQuery(config).RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.Items.Count, "dominated item must be absent from result");
            foreach (var item in result.Items)
                Assert.AreNotEqual(0f, item.Position.x, "x=0 was dominated and should be pruned");
        }

        [Test]
        public void Tick_LaterTestCanFilter_DominationDisabled_RealWinnerSurvives()
        {
            // test1 scores [0.1, 0.5, 1.0] for x=0,1,2; test2 (weight 0.5) filters the leader
            // x=2 but keeps the others. With unsound domination, x=0 (0.1) would be pruned after
            // test1 because 0.1 + 0.5 < 1.0 — and once test2 removes x=2, the pruned x=0 is gone
            // for good. With the filter-aware guard, domination is skipped and x=0 survives.
            var generator = new FixedPointsGenerator(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f));
            var scoreTest = new IndexedScoreTest(0.1f, 0.5f, 1.0f).Configure(weight: 1f);
            var filterTest = new IndexedScoreTest(0.5f, 0.5f, -1f).Configure(weight: 0.5f);
            var config = TestHelpers.MakeConfig(generator, scoreTest, filterTest);

            var result = new EQSQuery(config).RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(2, result.Items.Count, "x=2 filtered, x=0 and x=1 must both survive");
            bool hasZero = false;
            foreach (var item in result.Items)
                if (item.Position.x == 0f) hasZero = true;
            Assert.IsTrue(hasZero, "x=0 must not be pruned by domination when a later test filters");
        }

        // ---- Result safety ------------------------------------------------------

        [Test]
        public void TryGetBest_DefaultResult_ReturnsFalse()
        {
            EQSQueryResult result = default;

            Assert.IsFalse(result.TryGetBest(out _));
        }

        [Test]
        public void TopN_DefaultResult_WritesNothing()
        {
            EQSQueryResult result = default;
            var dst = new System.Collections.Generic.List<EQSScoredItem> { default };

            int written = result.TopN(3, dst);

            Assert.AreEqual(0, written);
            Assert.AreEqual(0, dst.Count);
        }

        [Test]
        public void TopN_StopsAtMinScore_AndRespectsCount()
        {
            var generator = new FixedPointsGenerator(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f));
            var result = new EQSQuery(TestHelpers.MakeConfig(generator, new IndexedScoreTest(0.1f, 0.6f, 1.0f)))
                .RunSync(TestHelpers.MakeContext(Vector3.zero));
            var dst = new System.Collections.Generic.List<EQSScoredItem>();

            int written = result.TopN(5, dst, minScore: 0.5f);

            Assert.AreEqual(2, written, "only x=2 (1.0) and x=1 (0.6) clear minScore");
            Assert.AreEqual(2f, dst[0].Position.x);
            Assert.AreEqual(1f, dst[1].Position.x);
        }

        [Test]
        public void Tick_CalledBeforeStart_Throws()
        {
            var config = TestHelpers.MakeConfig(
                new FixedPointsGenerator(Vector3.zero), new IndexedScoreTest(0.5f));
            var query = new EQSQuery(config);

            Assert.Throws<System.InvalidOperationException>(() => query.Tick());
        }

        // ---- Reuse / reset ------------------------------------------------------

        [Test]
        public void RunSync_CalledTwice_RecomputesFromCleanState()
        {
            var generator = new FixedPointsGenerator(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f));
            var config = TestHelpers.MakeConfig(generator, new IndexedScoreTest(0.3f, 0.7f));
            var query = new EQSQuery(config);

            // The result aliases the query's reused buffer (zero-alloc), so snapshot the first
            // run before the second one overwrites it.
            var first = query.RunSync(TestHelpers.MakeContext(Vector3.zero));
            var firstSnapshot = new System.Collections.Generic.List<EQSScoredItem>(first.Items);

            var second = query.RunSync(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(firstSnapshot.Count, second.Items.Count);
            for (int i = 0; i < firstSnapshot.Count; i++)
            {
                Assert.AreEqual(firstSnapshot[i].Position, second.Items[i].Position);
                Assert.AreEqual(firstSnapshot[i].Score, second.Items[i].Score, 1e-5f);
            }
        }

        // ---- EQSQuery.Items exposure -------------------------------------------

        [Test]
        public void Items_AfterStart_ExposesGeneratedItems()
        {
            var points = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f) };
            var config = TestHelpers.MakeConfig(new FixedPointsGenerator(points));
            var query = new EQSQuery(config);

            query.Start(TestHelpers.MakeContext(Vector3.zero));

            Assert.AreEqual(2, query.Items.Count);
            Assert.AreEqual(points[0], query.Items[0].Position);
            Assert.AreEqual(points[1], query.Items[1].Position);
        }
    }
}
