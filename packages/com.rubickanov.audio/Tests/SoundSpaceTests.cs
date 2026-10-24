using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Audio.Tests
{
    public class SoundSpaceTests
    {
        private readonly List<Object> _made = new();
        private SoundSpace _space = null!;
        private Transform _ears = null!;

        [SetUp]
        public void SetUp()
        {
            _space = new SoundSpace(new SoundSpaceSettings());
            _ears = Make("Ears").transform;
            _space.Listener = _ears;
        }

        [TearDown]
        public void TearDown()
        {
            _space.Dispose();
            foreach (Object o in _made)
                if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _made.Add(go);
            return go;
        }

        private SoundKind Kind(bool occlusion, float headShadow)
        {
            var kind = SoundKind.Create(occlusion: occlusion, headShadow: headShadow);
            _made.Add(kind);
            return kind;
        }

        private AudioSource Source(Vector3 at)
        {
            GameObject go = Make("Source");
            go.transform.position = at;
            return go.AddComponent<AudioSource>();
        }

        [Test]
        public void Tick_OccludedAfterOpen_Reaches63PercentAtAttackWithoutOvershoot()
        {
            bool blocked = false;
            _space.Blocked = (_, _, _) => blocked;
            var h = _space.Add(Source(Vector3.forward * 10f), Kind(occlusion: true, headShadow: 0f));
            blocked = true;

            float attack = _space.Settings.OcclusionAttack;
            const float dt = 0.002f;
            int steps = Mathf.RoundToInt(attack / dt);
            for (int i = 0; i < steps; i++) _space.Tick(dt);

            Assert.AreEqual(1f - Mathf.Exp(-1f), _space.OcclusionOf(h), 0.02f);
            for (int i = 0; i < 2000; i++)
            {
                _space.Tick(0.05f);
                Assert.LessOrEqual(_space.OcclusionOf(h), 1f);
            }
            Assert.AreEqual(-_space.Settings.OcclusionDb, 20f * Mathf.Log10(_space.GainOf(h)), 0.05f);
        }

        [Test]
        public void Tick_TurnedAway_ShadowFollowsTheOnePole()
        {
            var h = _space.Add(Source(Vector3.forward * 5f), Kind(occlusion: false, headShadow: 1f));
            Assert.AreEqual(1f, _space.GainOf(h), 1e-5f);
            _ears.rotation = Quaternion.Euler(0f, 180f, 0f);

            float tau = _space.Settings.ShadowSmoothing;
            const float dt = 0.001f;
            for (int i = 0; i < Mathf.RoundToInt(tau / dt); i++) _space.Tick(dt);

            float db = 20f * Mathf.Log10(_space.GainOf(h));
            Assert.AreEqual(-_space.Settings.ShadowDb * (1f - Mathf.Exp(-1f)), db, 0.03f);
            for (int i = 0; i < 1000; i++)
            {
                _space.Tick(0.05f);
                Assert.GreaterOrEqual(20f * Mathf.Log10(_space.GainOf(h)), -_space.Settings.ShadowDb - 1e-4f);
            }
        }

        [Test]
        public void Tick_TwentyOccludedWithBudgetEight_EveryOneCheckedWithinThreeFrames()
        {
            var checks = new Dictionary<Vector3, int>();
            _space.Settings.RaysPerFrame = 8;
            var kind = Kind(occlusion: true, headShadow: 0f);
            var silent = Kind(occlusion: false, headShadow: 1f);
            var points = new List<Vector3>();
            for (int i = 0; i < 20; i++)
            {
                var at = new Vector3(i + 1, 0f, 3f);
                points.Add(at);
                _space.Add(Source(at), kind);
                _space.Add(Source(at + Vector3.up * 50f), silent);
            }
            _space.Blocked = (_, to, _) =>
            {
                checks[to] = checks.TryGetValue(to, out int n) ? n + 1 : 1;
                return false;
            };

            for (int frame = 0; frame < 3; frame++) _space.Tick(0.016f);

            foreach (Vector3 p in points)
                Assert.That(checks.TryGetValue(p, out int n) ? n : 0, Is.GreaterThanOrEqualTo(1), $"source at {p}");
            int total = 0;
            foreach (int n in checks.Values) total += n;
            Assert.AreEqual(24, total, "8 rays per frame, only on occluded kinds");
        }

        [Test]
        public void Add_SameKindManyTimes_BuildsCurvesOnce()
        {
            var kind = Kind(occlusion: false, headShadow: 0f);
            _space.Add(Source(Vector3.forward), kind);
            int built = KindCurves.Built;

            for (int i = 0; i < 10; i++) _space.Add(Source(Vector3.forward * i), kind);

            Assert.AreEqual(built, KindCurves.Built);
        }

        [Test]
        public void SetVolume_ShadowedSource_SourceCarriesOwnedTimesGain()
        {
            _ears.rotation = Quaternion.Euler(0f, 180f, 0f);
            var source = Source(Vector3.forward * 5f);
            var h = _space.Add(source, Kind(occlusion: false, headShadow: 1f), 0.5f);

            _space.SetVolume(h, 0.8f);

            Assert.AreEqual(0.8f, _space.VolumeOf(h), 1e-6f);
            Assert.AreEqual(0.8f * SoundMath.DbToGain(-_space.Settings.ShadowDb), source.volume, 1e-4f);
        }

        [Test]
        public void Shape_KindWithoutReverb_BypassesZones()
        {
            var dry = SoundKind.Create(reverbAtReference: 0f, reverbMax: 0f);
            var wet = SoundKind.Create(reverbAtReference: 0.05f);
            _made.Add(dry);
            _made.Add(wet);
            AudioSource a = Source(Vector3.forward), b = Source(Vector3.forward);

            SoundSpace.Shape(a, dry);
            SoundSpace.Shape(b, wet);

            Assert.IsTrue(a.bypassReverbZones, "no reverb: out of the zones");
            Assert.IsFalse(b.bypassReverbZones);
            CollectionAssert.DoesNotContain(SoundSpace.Strays(), a);
        }
    }
}
