// The SoundConfig API stays for older users; the package itself still serves it.
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio.Tests
{
    [TestFixture]
    public class CuePickTests
    {
        private readonly List<UnityEngine.Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
                if (made != null) UnityEngine.Object.DestroyImmediate(made);
            _made.Clear();
        }

        [Test]
        public void Next_RandomNoRepeat_NeverRepeatsAndReachesAll()
        {
            var cue = Cue(4);
            var state = new CueState();
            var random = new SeededRandom(7);
            var seen = new int[4];
            int last = -1;

            for (int i = 0; i < 1000; i++)
            {
                int index = CuePick.Next(cue, ref state, -1, random);
                Assert.AreNotEqual(last, index, $"pick {i} repeated");
                seen[index]++;
                last = index;
            }

            Assert.IsTrue(seen.All(count => count > 150), string.Join(",", seen));
        }

        [Test]
        public void Next_OneVariant_AlwaysIt()
        {
            var cue = Cue(1);
            var state = new CueState();

            for (int i = 0; i < 5; i++)
                Assert.AreEqual(0, CuePick.Next(cue, ref state, -1, new SeededRandom(i)));
        }

        [Test]
        public void Next_EmptyCue_MinusOneAndStateUntouched()
        {
            var cue = Track(SoundCue.Create(Array.Empty<AudioResource>()));
            var state = new CueState();

            Assert.AreEqual(-1, CuePick.Next(cue, ref state, -1, new SeededRandom(1)));
            Assert.AreEqual(-1, CuePick.Next(cue, ref state, 3, new SeededRandom(1)));
            Assert.AreEqual(-1, state.Last);
        }

        [Test]
        public void Next_ExplicitIndex_WrapsAndBecomesLast()
        {
            var cue = Cue(3);
            var state = new CueState();

            Assert.AreEqual(1, CuePick.Next(cue, ref state, 7, new SeededRandom(1)));
            Assert.AreEqual(1, state.Last);
            Assert.AreEqual(1, CuePick.Next(cue, ref state, 1, new SeededRandom(1)), "an explicit index may repeat");
            Assert.AreNotEqual(1, CuePick.Next(cue, ref state, -1, new SeededRandom(1)), "no repeat after an explicit one");
        }

        [Test]
        public void Next_Sequence_GoesRound()
        {
            var cue = Cue(3, PickMode.Sequence);
            var state = new CueState();

            var picks = Enumerable.Range(0, 7).Select(_ => CuePick.Next(cue, ref state, -1, new SeededRandom(1))).ToArray();

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1, 2, 0 }, picks);
        }

        private SoundCue Cue(int variants, PickMode pick = PickMode.RandomNoRepeat) =>
            Track(SoundCue.Create(Enumerable.Range(0, variants).Select(_ => (AudioResource)Track(NewClip())).ToArray(), pick));

        private T Track<T>(T made) where T : UnityEngine.Object
        {
            _made.Add(made);
            return made;
        }

        internal static AudioClip NewClip() => AudioClip.Create("test", 1, 1, 44100, false);
    }

    [TestFixture]
    public class SoundCueServiceTests
    {
        private AudioServiceConfig _config = null!;
        private UnityAudioService _service = null!;
        private SoundSpace? _space;
        private readonly List<UnityEngine.Object> _made = new();

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<AudioServiceConfig>();
            _service = new UnityAudioService(_config);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
            _space?.Dispose();
            _space = null;
            foreach (var made in _made)
                if (made != null) UnityEngine.Object.DestroyImmediate(made);
            _made.Clear();
            UnityEngine.Object.DestroyImmediate(_config);
        }

        [Test]
        public void Play_NullCue_InvalidAndCountsNothing()
        {
            Assert.IsFalse(_service.Play(null).IsValid);
            Assert.IsFalse(_service.PlayAt(null, Vector3.zero).IsValid);
            Assert.IsFalse(_service.Loop(null).IsValid);

            var stats = new List<CueStats>();
            _service.Describe(stats);
            Assert.IsEmpty(stats);
            Assert.IsEmpty(Playing(_service));
        }

        [Test]
        public void Play_EmptyCue_Invalid()
        {
            var cue = Track(SoundCue.Create(Array.Empty<AudioResource>()));

            Assert.IsFalse(_service.Play(cue).IsValid);
            Assert.AreEqual(0, _service.Played(cue));
        }

        [Test]
        public void Play_MaxInstances_CountsOverAllVariantsAndDrops()
        {
            var cue = Cue(3, maxInstances: 2);

            Assert.IsTrue(_service.Play(cue, variant: 0).IsValid);
            Assert.IsTrue(_service.PlayAt(cue, Vector3.one, variant: 1).IsValid);
            Assert.IsFalse(_service.Play(cue, variant: 2).IsValid, "a third copy of any variant is over the limit");

            var stats = Stats(cue);
            Assert.AreEqual(2, stats.Played);
            Assert.AreEqual(1, stats.Dropped);
            Assert.AreEqual(2, stats.Playing);
        }

        [Test]
        public void Play_MaxInstancesAfterStop_PlaysAgain()
        {
            var cue = Cue(2, maxInstances: 1);
            var first = _service.Play(cue);

            _service.StopSound(first);

            Assert.IsTrue(_service.Play(cue).IsValid);
            Assert.AreEqual(1, Stats(cue).Playing);
        }

        [Test]
        public void Play_MinInterval_DropsUntilItPasses()
        {
            var clock = UseClock(_service);
            var cue = Cue(2, minInterval: 0.05f);

            Assert.IsTrue(_service.Play(cue).IsValid);
            clock.Now += 0.03;
            Assert.IsFalse(_service.Play(cue).IsValid);
            clock.Now += 0.03;
            Assert.IsTrue(_service.Play(cue).IsValid);

            Assert.AreEqual(2, Stats(cue).Played);
            Assert.AreEqual(1, Stats(cue).Dropped);
        }

        [Test]
        public void Play_ExplicitVariant_PlaysThatClipModulo()
        {
            var cue = Cue(3);

            _service.Play(cue, variant: 4);

            Assert.AreSame(cue.Variants[1], Playing(_service).Single().resource);
            Assert.AreEqual(1, Stats(cue).LastVariant);
        }

        [Test]
        public void Play_Volume_IsCueVolumeTimesCallClampedAtOne()
        {
            var cue = Cue(1, volume: 0.5f);

            _service.Play(cue, volume: 0.6f);
            _service.Play(cue, volume: 4f);

            var volumes = Playing(_service).Select(source => source.volume).ToArray();
            Assert.AreEqual(0.3f, volumes[0], 1e-5f);
            Assert.AreEqual(1f, volumes[1], 1e-5f);
        }

        [TestCase(0, 256)]
        [TestCase(50, 128)]
        [TestCase(100, 0)]
        public void PlayAt_WithKind_UnityPriorityFromScaleOverKind(int priority, int unity)
        {
            using var service = ServiceWithSpace(poolSize: 4);
            var kind = Track(SoundKind.Create(priority: 200));
            var cue = Cue(1, priority: priority, kind: kind);

            service.PlayAt(cue, Vector3.forward);

            Assert.AreEqual(unity, Playing(service).Single().priority);
        }

        [TestCase(0, 256)]
        [TestCase(50, 128)]
        [TestCase(100, 0)]
        public void Play_2DOnReusedSource_UnityPriorityFromScale(int priority, int unity)
        {
            using var service = ServiceWithPool(1);
            service.Play(Cue(1, priority: 0));
            var source = Playing(service).Single();
            // What someone else left on it; eviction hands the source over as it is.
            source.priority = 7;

            Assert.IsTrue(service.Play(Cue(1, priority: priority)).IsValid);

            Assert.AreSame(source, Playing(service).Single(), "the pool of one must reuse the source");
            Assert.AreEqual(unity, source.priority);
        }

        [Test]
        public void Play_OldSoundOnSourceACueLeft_GetsUnityDefault()
        {
            using var service = ServiceWithPool(1);
            service.Play(Cue(1, priority: SoundPriority.Feedback));
            var source = Playing(service).Single();
            Assert.AreEqual(0, source.priority);

            // Ranks 50 + 60 = 110 over the cue's 100: evicts it and takes the source as it is.
            Assert.IsTrue(service.PlaySFX(MakeSound(priority: 60)).IsValid);

            Assert.AreSame(source, Playing(service).Single());
            Assert.AreEqual(128, source.priority, "the old 2D path plays at Unity's default, as before");
        }

        [Test]
        public void Pool_FullOfCues_EvictsLowestPriorityFirst()
        {
            using var service = ServiceWithPool(2);
            var bed = Cue(1, priority: SoundPriority.Bed);
            var shot = Cue(1, priority: SoundPriority.Shot);
            var signal = Cue(1, priority: SoundPriority.Signal);
            service.Play(shot);
            service.Play(bed);

            Assert.IsTrue(service.Play(signal).IsValid);

            CollectionAssert.AreEquivalent(new[] { shot.Variants[0], signal.Variants[0] }, Resources(service));
            Assert.IsFalse(service.Play(bed).IsValid, "a bed below every playing sound does not start");
            Assert.AreEqual(1, Stats(bed, service).Dropped);
        }

        [Test]
        public void Pool_MixedApis_OldSoundRanksAsDefaultPlusItsPriority()
        {
            using var service = ServiceWithPool(2);
            var old = MakeSound(priority: 0);
            var signal = Cue(1, priority: SoundPriority.Signal);
            var bed = Cue(1, priority: SoundPriority.Bed);
            service.PlaySFX(old);
            service.Play(bed);

            Assert.IsTrue(service.Play(signal).IsValid, "a signal (70) evicts the bed (20), not the old voice (50)");
            CollectionAssert.AreEquivalent(new[] { old.Resource, signal.Variants[0] }, Resources(service));

            Assert.IsFalse(service.Play(bed).IsValid, "a bed (20) is below the old voice (50)");
            Assert.IsTrue(service.PlaySFX(MakeSound(priority: 0)).IsValid, "an old voice (50) evicts the old one, oldest first");
            CollectionAssert.Contains(Resources(service), signal.Variants[0]);
            CollectionAssert.DoesNotContain(Resources(service), old.Resource);
        }

        [Test]
        public void Loop_TwoOfOneCue_AreIndependent()
        {
            var cue = Cue(2, volume: 0.5f);
            var a = _service.Loop(cue);
            var b = _service.Loop(cue, volume: 0.4f);

            _service.SetVolume(a, 0.8f);
            _service.Stop(b);

            Assert.AreNotEqual(a, b);
            Assert.AreEqual(0.4f, LoopSourceOf(_service, a).volume, 1e-5f, "SetVolume scales cue.Volume");
            Assert.AreEqual(1, LoopSlots(_service).Count);
            Assert.AreEqual(1, Stats(cue).Playing);
            Assert.AreEqual(2, Stats(cue).Played);
        }

        [Test]
        public void Loop_StaleHandle_Ignored()
        {
            var cue = Cue(1);
            var old = _service.Loop(cue);
            _service.Stop(old);
            var fresh = _service.Loop(cue, volume: 0.5f);

            _service.SetVolume(old, 0.1f);
            _service.SetPitch(old, 2f);
            _service.Stop(old);

            Assert.AreNotEqual(old, fresh, "ids are never reused");
            Assert.IsFalse(_service.IsPlaying(old));
            var source = LoopSourceOf(_service, fresh);
            Assert.AreEqual(0.5f, source.volume, 1e-5f);
            Assert.AreEqual(1f, source.pitch, 1e-5f);
        }

        [Test]
        public void Loop_SetVolume_ClampedAtOne()
        {
            var cue = Cue(1, volume: 0.5f);
            var loop = _service.Loop(cue);

            _service.SetVolume(loop, 3f);

            Assert.AreEqual(1f, LoopSourceOf(_service, loop).volume, 1e-5f);
        }

        [Test]
        public void Loop_FadingOut_IsNotPlayingAndSetVolumeDoesNotRevive()
        {
            var cue = Cue(1);
            var loop = _service.Loop(cue);
            var source = LoopSourceOf(_service, loop);

            _service.Stop(loop, fadeOut: 10f);
            _service.SetVolume(loop, 1f);

            Assert.IsFalse(_service.IsPlaying(loop));
            Assert.AreEqual(1f, source.volume, 0.05f, "still fading from full");
            _service.Stop(loop);
            Assert.IsEmpty(LoopSlots(_service), "a cut ends a fading loop");
            Assert.AreEqual(0, Stats(cue).Playing);
        }

        [Test]
        public void Adopt_ThenRelease_SetsAndUndoesMix()
        {
            using var service = ServiceWithSpace(poolSize: 1);
            var go = Track(new GameObject("Own"));
            var source = go.AddComponent<AudioSource>();
            var kind = Track(SoundKind.Create(priority: 200));

            var handle = service.Adopt(source, new SourceSetup(kind, SoundPriority.Voice));

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(26, source.priority);
            Assert.AreEqual(1, _space!.Count);
            service.Release(source);
            Assert.AreEqual(0, _space.Count);
            Assert.AreEqual(128, source.priority);
        }

        [Test]
        public void Release_OfDestroyedSource_ForgetsIt()
        {
            using var service = ServiceWithSpace(poolSize: 1);
            var go = new GameObject("Own");
            var source = go.AddComponent<AudioSource>();
            var kind = Track(SoundKind.Create());
            service.Adopt(source, new SourceSetup(kind, SoundPriority.Engine));
            Assert.IsTrue(service.IsAdopted(source));

            UnityEngine.Object.DestroyImmediate(go);
            service.Release(source);

            Assert.IsFalse(service.IsAdopted(source));
            Assert.AreEqual(0, _space!.Count);
        }

        [Test]
        public void GroupOf_CueThenKindThenSfx()
        {
            Assert.IsNull(_service.GroupOf(Cue(1)), "the test config has no SFX group");
        }

        private UnityAudioService ServiceWithPool(int size)
        {
            SetBacking(_config, "MaxSfxSources", size);
            return new UnityAudioService(_config);
        }

        private UnityAudioService ServiceWithSpace(int poolSize)
        {
            SetBacking(_config, "MaxSfxSources", poolSize);
            _space = new SoundSpace(new SoundSpaceSettings());
            return new UnityAudioService(_config, _space);
        }

        private SoundCue Cue(int variants, int priority = SoundPriority.Default, int maxInstances = 0, float minInterval = 0f,
            float volume = 1f, SoundKind? kind = null) =>
            Track(SoundCue.Create(Enumerable.Range(0, variants).Select(_ => (AudioResource)Track(CuePickTests.NewClip())).ToArray(),
                volume: volume, kind: kind, priority: priority, maxInstances: maxInstances, minInterval: minInterval));

        private SoundConfig MakeSound(int priority)
        {
            object boxed = default(SoundConfig);
            typeof(SoundConfig).GetField("_resource", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(boxed, Track(CuePickTests.NewClip()));
            typeof(SoundConfig).GetField("_priority", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(boxed, priority);
            return (SoundConfig)boxed;
        }

        private CueStats Stats(SoundCue cue, UnityAudioService? on = null)
        {
            var stats = new List<CueStats>();
            (on ?? _service).Describe(stats);
            return stats.Single(s => s.Cue == cue);
        }

        private T Track<T>(T made) where T : UnityEngine.Object
        {
            _made.Add(made);
            return made;
        }

        private static AudioSource[] Playing(UnityAudioService service) =>
            GetField<System.Collections.IEnumerable>(service, "_activeSources").Cast<AudioSource>().ToArray();

        private static AudioResource[] Resources(UnityAudioService service) =>
            Playing(service).Select(source => source.resource).ToArray();

        private static Dictionary<string, AudioSource> LoopSlots(UnityAudioService service) =>
            GetField<Dictionary<string, AudioSource>>(service, "_loopSources");

        private static AudioSource LoopSourceOf(UnityAudioService service, LoopHandle loop)
        {
            var keys = GetField<Dictionary<string, long>>(service, "_loopIds");
            long id = (long)typeof(LoopHandle).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop);
            return LoopSlots(service)[keys.Single(pair => pair.Value == id).Key];
        }

        private static FakeClock UseClock(UnityAudioService service)
        {
            var clock = new FakeClock();
            typeof(UnityAudioService).GetField("_now", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(service, new Func<double>(() => clock.Now));
            return clock;
        }

        private sealed class FakeClock
        {
            public double Now = 100;
        }

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);

        private static void SetBacking(AudioServiceConfig config, string property, object value) =>
            typeof(AudioServiceConfig)
                .GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(config, value);
    }

    internal sealed class SeededRandom : ICueRandom
    {
        private readonly System.Random _random;

        public SeededRandom(int seed) => _random = new System.Random(seed);

        public int Next(int count) => _random.Next(count);
    }
}
