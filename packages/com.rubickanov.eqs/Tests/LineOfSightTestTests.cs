using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.EQS.Tests
{
    [TestFixture]
    public class LineOfSightTestTests
    {
        // Layer 30 is unused by default Unity layers — using it isolates tests from
        // any stray colliders the editor might have lying around.
        private const int TestLayer = 30;
        private static readonly LayerMask TestLayerMask = 1 << TestLayer;

        private List<GameObject> _spawned = null!;

        [SetUp]
        public void SetUp()
        {
            _spawned = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        // ---- Score (per-item) ---------------------------------------------------

        [Test]
        public void Score_NoObstacle_ReturnsOne()
        {
            var test = MakeLosTest();
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var item = new EQSItem(new Vector3(5f, 0f, 0f));

            float score = test.Score(ctx, item);

            Assert.AreEqual(1f, score);
        }

        [Test]
        public void Score_BlockedByCollider_ReturnsNegativeOneWhenFilterOnFail()
        {
            var test = MakeLosTest(filterOnFail: true);
            CreateBlocker(new Vector3(2.5f, 0f, 0f), new Vector3(1f, 4f, 4f));
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var item = new EQSItem(new Vector3(5f, 0f, 0f));

            float score = test.Score(ctx, item);

            Assert.AreEqual(-1f, score);
        }

        [Test]
        public void Score_BlockedByCollider_ReturnsZeroWhenFilterOff()
        {
            var test = MakeLosTest(filterOnFail: false);
            CreateBlocker(new Vector3(2.5f, 0f, 0f), new Vector3(1f, 4f, 4f));
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var item = new EQSItem(new Vector3(5f, 0f, 0f));

            float score = test.Score(ctx, item);

            Assert.AreEqual(0f, score);
        }

        [Test]
        public void Score_BlockerOnDifferentLayer_ReturnsOne()
        {
            // Sanity: confirms our layer mask actually filters. If this fails the
            // physics-blocked tests below would be untrustworthy too.
            var test = MakeLosTest();
            var blocker = CreateBlocker(new Vector3(2.5f, 0f, 0f), new Vector3(1f, 4f, 4f));
            blocker.layer = 0; // outside TestLayerMask
            Physics.SyncTransforms();

            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var item = new EQSItem(new Vector3(5f, 0f, 0f));

            float score = test.Score(ctx, item);

            Assert.AreEqual(1f, score);
        }

        [Test]
        public void Score_DegenerateDistance_ReturnsOneWithoutRaycast()
        {
            // A blocker is sitting right at the querier — if the degenerate-distance
            // early return isn't honoured, the raycast would hit it.
            var test = MakeLosTest();
            CreateBlocker(Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f));
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var item = new EQSItem(new Vector3(0.005f, 0f, 0f));

            float score = test.Score(ctx, item);

            Assert.AreEqual(1f, score);
        }

        [Test]
        public void Score_HonoursEyeHeightAboveBlocker_ReturnsOne()
        {
            // Blocker is short; the eye sits above it, so the ray clears.
            var test = MakeLosTest(eyeHeight: 2f, targetHeight: 2f);
            CreateBlocker(new Vector3(2.5f, 0f, 0f), new Vector3(1f, 0.5f, 4f));
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var item = new EQSItem(new Vector3(5f, 0f, 0f));

            float score = test.Score(ctx, item);

            Assert.AreEqual(1f, score);
        }

        // ---- ScoreBatch ---------------------------------------------------------

        [Test]
        public void ScoreBatch_NoObstacles_AllItemsScoreOne()
        {
            var test = MakeLosTest();
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var items = new List<EQSItem>
            {
                new EQSItem(new Vector3(5f, 0f, 0f)),
                new EQSItem(new Vector3(0f, 0f, 5f)),
                new EQSItem(new Vector3(-5f, 0f, 0f)),
            };
            var alive = new[] { true, true, true };
            var rawScores = new float[3];

            test.ScoreBatch(ctx, items, alive, rawScores, 0, items.Count);

            Assert.AreEqual(1f, rawScores[0]);
            Assert.AreEqual(1f, rawScores[1]);
            Assert.AreEqual(1f, rawScores[2]);
        }

        [Test]
        public void ScoreBatch_MixedHitsAndMisses_ParityWithScore()
        {
            var test = MakeLosTest();
            CreateBlocker(new Vector3(2.5f, 0f, 0f), new Vector3(1f, 4f, 4f));
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var items = new List<EQSItem>
            {
                new EQSItem(new Vector3(5f, 0f, 0f)),  // blocked along +X
                new EQSItem(new Vector3(0f, 0f, 5f)),  // clear along +Z
                new EQSItem(new Vector3(-5f, 0f, 0f)), // clear along -X
            };
            var alive = new[] { true, true, true };
            var batchScores = new float[3];

            test.ScoreBatch(ctx, items, alive, batchScores, 0, items.Count);

            for (int i = 0; i < items.Count; i++)
                Assert.AreEqual(
                    test.Score(ctx, items[i]), batchScores[i],
                    $"item {i}: batch and per-item paths disagree");
        }

        [Test]
        public void ScoreBatch_AllDead_DoesNotWriteRawScores()
        {
            var test = MakeLosTest();
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var items = new List<EQSItem>
            {
                new EQSItem(new Vector3(5f, 0f, 0f)),
                new EQSItem(new Vector3(0f, 0f, 5f)),
            };
            var alive = new[] { false, false };
            var rawScores = new[] { -42f, -42f };

            test.ScoreBatch(ctx, items, alive, rawScores, 0, items.Count);

            Assert.AreEqual(-42f, rawScores[0]);
            Assert.AreEqual(-42f, rawScores[1]);
        }

        [Test]
        public void ScoreBatch_DeadItemMixedWithLive_LeavesDeadSlotsUntouched()
        {
            var test = MakeLosTest();
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var items = new List<EQSItem>
            {
                new EQSItem(new Vector3(5f, 0f, 0f)),
                new EQSItem(new Vector3(0f, 0f, 5f)),
                new EQSItem(new Vector3(-5f, 0f, 0f)),
            };
            var alive = new[] { true, false, true };
            var rawScores = new[] { 0f, -42f, 0f };

            test.ScoreBatch(ctx, items, alive, rawScores, 0, 3);

            Assert.AreEqual(1f, rawScores[0]);
            Assert.AreEqual(-42f, rawScores[1], "dead slot must remain untouched");
            Assert.AreEqual(1f, rawScores[2]);
        }

        [Test]
        public void ScoreBatch_DegenerateDistance_ScoredInlineWithoutRaycast()
        {
            // Mirrors the per-item degenerate test: the blocker sits right at the
            // querier so a raycast would hit, but the inline early-return saves us.
            var test = MakeLosTest();
            CreateBlocker(Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f));
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var items = new List<EQSItem>
            {
                new EQSItem(new Vector3(0.005f, 0f, 0f)),
            };
            var alive = new[] { true };
            var rawScores = new float[1];

            test.ScoreBatch(ctx, items, alive, rawScores, 0, 1);

            Assert.AreEqual(1f, rawScores[0]);
        }

        [Test]
        public void ScoreBatch_RangeSubset_OnlyTouchesItemsInRange()
        {
            var test = MakeLosTest();
            var ctx = TestHelpers.MakeContext(Vector3.zero);
            var items = new List<EQSItem>
            {
                new EQSItem(new Vector3(5f, 0f, 0f)),
                new EQSItem(new Vector3(0f, 0f, 5f)),
                new EQSItem(new Vector3(-5f, 0f, 0f)),
                new EQSItem(new Vector3(0f, 0f, -5f)),
            };
            var alive = new[] { true, true, true, true };
            var rawScores = new[] { -1f, -1f, -1f, -1f };

            test.ScoreBatch(ctx, items, alive, rawScores, 1, 3);

            Assert.AreEqual(-1f, rawScores[0], "index < startIndex must be untouched");
            Assert.AreEqual(1f, rawScores[1]);
            Assert.AreEqual(1f, rawScores[2]);
            Assert.AreEqual(-1f, rawScores[3], "index >= endExclusive must be untouched");
        }

        // ---- Helpers ------------------------------------------------------------

        private GameObject CreateBlocker(Vector3 position, Vector3 size)
        {
            var go = new GameObject("LosBlocker") { layer = TestLayer };
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            _spawned.Add(go);
            // EditMode tests don't tick physics, so push transforms into the
            // physics scene manually before any raycast queries.
            Physics.SyncTransforms();
            return go;
        }

        private static LineOfSightTest MakeLosTest(
            float eyeHeight = 0f,
            float targetHeight = 0f,
            bool filterOnFail = true)
        {
            var test = new LineOfSightTest();
            var t = typeof(LineOfSightTest);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            t.GetField("_eyeHeight", flags)!.SetValue(test, eyeHeight);
            t.GetField("_targetHeight", flags)!.SetValue(test, targetHeight);
            t.GetField("_obstacleMask", flags)!.SetValue(test, TestLayerMask);
            t.GetField("_filterOnFail", flags)!.SetValue(test, filterOnFail);
            return test;
        }
    }
}
