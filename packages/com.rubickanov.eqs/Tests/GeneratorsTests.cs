using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.EQS.Tests
{
    [TestFixture]
    public class GridGeneratorTests
    {
        private const int TestLayer = 30;
        private static readonly LayerMask TestLayerMask = 1 << TestLayer;

        private List<GameObject> _spawned = null!;

        [SetUp]
        public void SetUp() => _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        [Test]
        public void Generate_NoProjection_ProducesGridCenteredAtQuerier()
        {
            var generator = MakeGrid(halfExtent: 4f, spacing: 2f, projectToGround: false);
            var ctx = new EQSQueryContext(new Vector3(10f, 0f, 10f), Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            // Range -4..4 stepping 2 → {-4, -2, 0, 2, 4} = 5 values per axis → 25 points.
            Assert.AreEqual(25, results.Count);
            // Centered: every point sits in [center-4, center+4] on X and Z.
            foreach (var item in results)
            {
                Assert.GreaterOrEqual(item.Position.x, 10f - 4f - 1e-4f);
                Assert.LessOrEqual(item.Position.x, 10f + 4f + 1e-4f);
                Assert.GreaterOrEqual(item.Position.z, 10f - 4f - 1e-4f);
                Assert.LessOrEqual(item.Position.z, 10f + 4f + 1e-4f);
                Assert.AreEqual(0f, item.Position.y);
            }
        }

        [Test]
        public void Generate_NoProjection_PointCountMatchesExtentAndSpacing()
        {
            var generator = MakeGrid(halfExtent: 6f, spacing: 3f, projectToGround: false);
            var ctx = new EQSQueryContext(Vector3.zero, Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            // -6, -3, 0, 3, 6 = 5 per axis → 25 points.
            Assert.AreEqual(25, results.Count);
        }

        [Test]
        public void Generate_NonDivisibleSpacing_ProducesSymmetricDeterministicGrid()
        {
            // halfExtent not a multiple of spacing: floor(5/2)=2 steps → {-4,-2,0,2,4} per axis.
            // Guards against the old float-accumulator loop dropping a row to rounding.
            var generator = MakeGrid(halfExtent: 5f, spacing: 2f, projectToGround: false);
            var ctx = new EQSQueryContext(Vector3.zero, Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(25, results.Count);
            foreach (var item in results)
            {
                Assert.LessOrEqual(Mathf.Abs(item.Position.x), 4f + 1e-4f);
                Assert.LessOrEqual(Mathf.Abs(item.Position.z), 4f + 1e-4f);
            }
        }

        [Test]
        public void Generate_ProjectToGroundOverFloor_AllPointsLandOnFloorY()
        {
            // Big floor under everything; raycasts should land on y=0.
            CreateFloor(new Vector3(0f, 0f, 0f), new Vector3(100f, 1f, 100f));

            var generator = MakeGrid(halfExtent: 4f, spacing: 2f, projectToGround: true);
            var ctx = new EQSQueryContext(new Vector3(0f, 5f, 0f), Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            // BoxCollider top sits at y = 0 + 0.5 (centred at 0, half-height 0.5).
            Assert.AreEqual(25, results.Count);
            foreach (var item in results)
                Assert.AreEqual(0.5f, item.Position.y, 1e-3f);
        }

        [Test]
        public void Generate_ProjectToGroundNoColliders_ProducesNoItems()
        {
            var generator = MakeGrid(halfExtent: 2f, spacing: 1f, projectToGround: true);
            var ctx = new EQSQueryContext(Vector3.zero, Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Generate_ProjectToGroundCollidersOnDifferentLayer_ProducesNoItems()
        {
            // Sanity that ground mask actually filters layers.
            var floor = CreateFloor(Vector3.zero, new Vector3(100f, 1f, 100f));
            floor.layer = 0; // outside TestLayerMask
            Physics.SyncTransforms();

            var generator = MakeGrid(halfExtent: 2f, spacing: 1f, projectToGround: true);
            var ctx = new EQSQueryContext(Vector3.zero, Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(0, results.Count);
        }

        // ---- Helpers ------------------------------------------------------------

        private GameObject CreateFloor(Vector3 center, Vector3 size)
        {
            var go = new GameObject("Floor") { layer = TestLayer };
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            _spawned.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        private static GridGenerator MakeGrid(float halfExtent, float spacing, bool projectToGround)
        {
            var generator = new GridGenerator();
            var t = typeof(GridGenerator);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            t.GetField("_halfExtent", flags)!.SetValue(generator, halfExtent);
            t.GetField("_spacing", flags)!.SetValue(generator, spacing);
            t.GetField("_projectToGround", flags)!.SetValue(generator, projectToGround);
            t.GetField("_groundMask", flags)!.SetValue(generator, TestLayerMask);
            t.GetField("_raycastHeight", flags)!.SetValue(generator, 50f);
            return generator;
        }
    }

    [TestFixture]
    public class CircleGeneratorTests
    {
        private const int TestLayer = 30;
        private static readonly LayerMask TestLayerMask = 1 << TestLayer;

        private List<GameObject> _spawned = null!;

        [SetUp]
        public void SetUp() => _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        [Test]
        public void Generate_NoProjection_ProducesPointCountPointsOnRadius()
        {
            var generator = MakeCircle(radius: 5f, pointCount: 8);
            var ctx = new EQSQueryContext(Vector3.zero, Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(8, results.Count);
            foreach (var item in results)
            {
                float distXz = new Vector2(item.Position.x, item.Position.z).magnitude;
                Assert.AreEqual(5f, distXz, 1e-4f, "every point must lie on the circle");
                Assert.AreEqual(0f, item.Position.y);
            }
        }

        [Test]
        public void Generate_NoProjection_CenteredAtQuerier()
        {
            var generator = MakeCircle(radius: 3f, pointCount: 6);
            var ctx = new EQSQueryContext(new Vector3(10f, 0f, -7f), Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            foreach (var item in results)
            {
                float dx = item.Position.x - 10f;
                float dz = item.Position.z - (-7f);
                Assert.AreEqual(3f, Mathf.Sqrt(dx * dx + dz * dz), 1e-4f);
            }
        }

        [Test]
        public void Generate_PointCountZero_ProducesNoItems()
        {
            var generator = MakeCircle(radius: 5f, pointCount: 0);
            var ctx = new EQSQueryContext(Vector3.zero, Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Generate_AroundReferenceWithReferencePosition_CenteredOnReference()
        {
            var generator = MakeCircle(radius: 4f, pointCount: 4, aroundReference: true);
            var ctx = new EQSQueryContext(
                position: new Vector3(100f, 0f, 100f),
                forward: Vector3.forward,
                referencePosition: new Vector3(20f, 0f, -5f));
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(4, results.Count);
            foreach (var item in results)
            {
                float dx = item.Position.x - 20f;
                float dz = item.Position.z - (-5f);
                Assert.AreEqual(4f, Mathf.Sqrt(dx * dx + dz * dz), 1e-4f);
            }
        }

        [Test]
        public void Generate_AroundReferenceWithoutReferencePosition_FallsBackToQuerier()
        {
            var generator = MakeCircle(radius: 4f, pointCount: 4, aroundReference: true);
            var ctx = new EQSQueryContext(
                position: new Vector3(50f, 0f, 50f),
                forward: Vector3.forward); // referencePosition is null
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            foreach (var item in results)
            {
                float dx = item.Position.x - 50f;
                float dz = item.Position.z - 50f;
                Assert.AreEqual(4f, Mathf.Sqrt(dx * dx + dz * dz), 1e-4f);
            }
        }

        [Test]
        public void Generate_ProjectToGroundOverFloor_AllPointsLandOnFloorY()
        {
            CreateFloor(Vector3.zero, new Vector3(100f, 1f, 100f));
            var generator = MakeCircle(radius: 5f, pointCount: 8, projectToGround: true);
            var ctx = new EQSQueryContext(new Vector3(0f, 5f, 0f), Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(8, results.Count);
            foreach (var item in results)
                Assert.AreEqual(0.5f, item.Position.y, 1e-3f);
        }

        [Test]
        public void Generate_ProjectToGroundNoColliders_ProducesNoItems()
        {
            var generator = MakeCircle(radius: 5f, pointCount: 8, projectToGround: true);
            var ctx = new EQSQueryContext(Vector3.zero, Vector3.forward);
            var results = new List<EQSItem>();

            generator.Generate(ctx, results);

            Assert.AreEqual(0, results.Count);
        }

        // ---- Helpers ------------------------------------------------------------

        private GameObject CreateFloor(Vector3 center, Vector3 size)
        {
            var go = new GameObject("Floor") { layer = TestLayer };
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            _spawned.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        private static CircleGenerator MakeCircle(
            float radius, int pointCount,
            bool aroundReference = false, bool projectToGround = false)
        {
            var generator = new CircleGenerator();
            var t = typeof(CircleGenerator);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            t.GetField("_radius", flags)!.SetValue(generator, radius);
            t.GetField("_pointCount", flags)!.SetValue(generator, pointCount);
            t.GetField("_aroundReference", flags)!.SetValue(generator, aroundReference);
            t.GetField("_projectToGround", flags)!.SetValue(generator, projectToGround);
            t.GetField("_groundMask", flags)!.SetValue(generator, TestLayerMask);
            t.GetField("_raycastHeight", flags)!.SetValue(generator, 50f);
            return generator;
        }
    }
}
