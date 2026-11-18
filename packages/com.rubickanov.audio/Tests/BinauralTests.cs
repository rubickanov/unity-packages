using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Audio.Tests
{
    // Binaural without the native plugin (Pretend): what SoundSpace gives a source when HRTF applies or not.
    public class BinauralTests
    {
        private readonly List<Object> _made = new();
        private SoundSpace _space = null!;
        private Transform _ears = null!;

        [SetUp]
        public void SetUp()
        {
            Binaural.Pretend(false);
            Binaural.On = true;
            _space = new SoundSpace(new SoundSpaceSettings());
            _ears = Make("Ears").transform;
            _space.Listener = _ears;
        }

        [TearDown]
        public void TearDown()
        {
            Binaural.Pretend(false);
            Binaural.On = true;
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

        private SoundKind Kind(bool binaural)
        {
            var kind = SoundKind.Create(spreadNear: 150f, spreadFar: 60f, binaural: binaural);
            _made.Add(kind);
            return kind;
        }

        private AudioSource Source(Vector3 at)
        {
            GameObject go = Make("Source");
            go.transform.position = at;
            return go.AddComponent<AudioSource>();
        }

        private static readonly float[] Distances = { 0.5f, 2f, 5f, 12f, 30f, 55f };

        private static void AssertRolloff(AudioSource source, SoundKind kind, bool binaural)
        {
            AnimationCurve curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            foreach (float d in Distances)
            {
                float want = binaural ? SoundMath.LevelBinaural(d, kind) : SoundMath.Level(d, kind);
                Assert.AreEqual(want, curve.Evaluate(d / kind.Max), 0.02f * want + 1e-4f, $"at {d} m");
            }
        }

        [Test]
        public void Applies_OnlyReadyOnAndBinaural()
        {
            SoundKind binaural = Kind(true), panned = Kind(false);
            Assert.IsFalse(Binaural.Applies(binaural), "not ready");
            Binaural.Pretend(true);
            Assert.IsTrue(Binaural.Applies(binaural));
            Assert.IsFalse(Binaural.Applies(panned));
            Binaural.On = false;
            Assert.IsFalse(Binaural.Applies(binaural), "switched off");
        }

        [Test]
        public void ShapeBinaural_KindLessSource_FollowsReadyAndOn_KeepsItsCurve_UnshapeLeavesItPanned()
        {
            AudioSource source = Source(Vector3.right * 3f);
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            SoundSpace.ShapeBinaural(source);
            Assert.IsFalse(source.spatialize, "not ready");
            Binaural.Pretend(true);
            Assert.IsTrue(source.spatialize);
            Assert.IsTrue(source.spatializePostEffects);
            Binaural.On = false;
            Assert.IsFalse(source.spatialize, "switched off");
            Binaural.On = true;
            Assert.IsTrue(source.spatialize, "back on");
            SoundSpace.UnshapeBinaural(source);
            Assert.IsFalse(source.spatialize);
            Binaural.On = false;
            Binaural.On = true;
            Assert.IsFalse(source.spatialize, "forgotten");
            Assert.AreEqual(AudioRolloffMode.Logarithmic, source.rolloffMode);
        }

        [Test]
        public void LevelBinaural_IsThePannedLoudness()
        {
            // A panned source is heard at its rolloff × Unity's spread loss; HRTF has no spread loss.
            SoundKind kind = Kind(true);
            foreach (float d in Distances)
            {
                float heard = SoundMath.Level(d, kind) / SoundMath.SpreadCompensation(SoundMath.Spread(d, kind));
                Assert.AreEqual(heard, SoundMath.LevelBinaural(d, kind), 1e-5f, $"at {d} m");
                Assert.LessOrEqual(SoundMath.LevelBinaural(d, kind), SoundMath.Level(d, kind) + 1e-6f);
            }
        }

        [Test]
        public void Shape_NotReady_BinauralKindStaysPannedOnItsRolloff()
        {
            SoundKind kind = Kind(true);
            AudioSource source = Source(Vector3.forward * 3f);
            source.spatialize = true;
            SoundSpace.Shape(source, kind);
            Assert.IsFalse(source.spatialize);
            Assert.IsFalse(source.spatializePostEffects);
            AssertRolloff(source, kind, binaural: false);
        }

        [Test]
        public void Shape_Ready_BinauralKindGetsTheUncompensatedRolloff_OtherKindKeepsItsOwn()
        {
            Binaural.Pretend(true);
            SoundKind binaural = Kind(true), panned = Kind(false);
            AudioSource a = Source(Vector3.forward * 3f), b = Source(Vector3.right * 3f);
            SoundSpace.Shape(a, binaural);
            SoundSpace.Shape(b, panned);
            Assert.IsTrue(a.spatialize);
            Assert.IsTrue(a.spatializePostEffects);
            AssertRolloff(a, binaural, binaural: true);
            Assert.IsFalse(b.spatialize);
            AssertRolloff(b, panned, binaural: false);
        }

        [Test]
        public void Reshape_OnToggleAndStop_SourceSwitchesSpatializerAndRolloff()
        {
            SoundKind kind = Kind(true);
            AudioSource source = Source(Vector3.forward * 3f);
            SoundSpace.Shape(source, kind);
            Binaural.Pretend(true);
            Assert.IsTrue(source.spatialize, "started");
            AssertRolloff(source, kind, binaural: true);
            Binaural.On = false;
            Assert.IsFalse(source.spatialize, "switched off");
            AssertRolloff(source, kind, binaural: false);
            Binaural.On = true;
            Assert.IsTrue(source.spatialize, "switched on");
            Binaural.Pretend(false);
            Assert.IsFalse(source.spatialize, "stopped");
            AssertRolloff(source, kind, binaural: false);
        }

        [Test]
        public void Unshape_ThenNonBinauralKind_PooledSourceLeavesTheSpatializer()
        {
            Binaural.Pretend(true);
            AudioSource source = Source(Vector3.forward * 3f);
            SoundSpace.Shape(source, Kind(true));
            SoundSpace.Unshape(source);
            Assert.IsFalse(source.spatialize);
            SoundKind panned = Kind(false);
            SoundSpace.Shape(source, panned);
            Assert.IsFalse(source.spatialize);
            AssertRolloff(source, panned, binaural: false);
        }

        [Test]
        public void Tick_Behind_NoHeadShadowOnHrtf_ShadowBackWhenOff()
        {
            Binaural.Pretend(true);
            _ears.rotation = Quaternion.Euler(0f, 180f, 0f);
            var h = _space.Add(Source(Vector3.forward * 5f), Kind(true));
            for (int i = 0; i < 200; i++) _space.Tick(0.05f);
            Assert.AreEqual(1f, _space.GainOf(h), 1e-4f, "HRTF");

            Binaural.On = false;
            for (int i = 0; i < 200; i++) _space.Tick(0.05f);
            Assert.AreEqual(-_space.Settings.ShadowDb, 20f * Mathf.Log10(_space.GainOf(h)), 0.05f, "panned");
        }
    }
}
