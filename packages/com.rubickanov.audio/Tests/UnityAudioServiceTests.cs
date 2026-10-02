using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace Rubickanov.Audio.Tests
{
    [TestFixture]
    public class UnityAudioServiceTests
    {
        private const string TestMixerPath = "Packages/com.rubickanov.audio/Tests/TestMixer.mixer";

        private AudioServiceConfig _config = null!;
        private UnityAudioService _service = null!;
        private readonly System.Collections.Generic.List<GameObject> _targets = new();

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<AudioServiceConfig>();
            _service = new UnityAudioService(_config);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            foreach (var target in _targets)
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
            _targets.Clear();
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
        }

        [Test]
        public void Constructor_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new UnityAudioService(null!));
        }

        [Test]
        public void PlaySFX_InvalidConfig_ReturnsInvalidHandle()
        {
            var handle = _service.PlaySFX(default);

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void PlaySFX_PoolEmptyAndAllSourcesFadingOut_TakesFadingSource()
        {
            using var service = ServiceWithPool(1);
            var sound = MakeValidSound();
            var first = service.PlaySFX(sound);
            service.StopSound(first, fadeOut: 10f);

            SoundHandle second = default;
            Assert.DoesNotThrow(() => second = service.PlaySFX(sound),
                "RentSource must not dereference an empty active list when every source is fading out");

            Assert.IsTrue(second.IsValid);
            Assert.AreEqual(1, SfxSources(service).Length, "the pool must not grow past its cap");
        }

        [Test]
        public void PlaySFX_AfterStopAllSFXWithFadeOut_StaysWithinCapAndEvictsAsUsual()
        {
            using var service = ServiceWithPool(2);
            service.PlaySFX(MakeSound(NewClip()));
            service.PlaySFX(MakeSound(NewClip()));
            service.StopAllSFX(fadeOut: 1000f);
            var first = MakeSound(NewClip());
            var second = MakeSound(NewClip());
            var third = MakeSound(NewClip());

            service.PlaySFX(first);
            service.PlaySFX(second);
            service.PlaySFX(third);

            Assert.AreEqual(2, SfxSources(service).Length);
            CollectionAssert.AreEqual(new[] { second.Resource, third.Resource }, PlayingResources(service));
        }

        [Test]
        public void PlaySFX_SeveralSourcesFadingOut_TakesQuietest()
        {
            using var service = ServiceWithPool(2);
            var loud = MakeSound(NewClip());
            var quiet = MakeSound(NewClip());
            service.PlaySFX(loud, volumeScale: 1f);
            service.PlaySFX(quiet, volumeScale: 0.2f);
            service.StopAllSFX(fadeOut: 1000f);

            service.PlaySFX(MakeSound(NewClip()));

            var held = SfxSources(service).Select(source => source.resource).ToArray();
            CollectionAssert.Contains(held, loud.Resource, "the louder fade must go on");
            CollectionAssert.DoesNotContain(held, quiet.Resource);
        }

        [Test]
        public void PlaySFXAtPoint_InvalidConfig_ReturnsInvalidHandle()
        {
            var handle = _service.PlaySFXAtPoint(default, Vector3.zero);

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void PlaySFXAttached_NullTransform_ReturnsInvalidHandle()
        {
            var handle = _service.PlaySFXAttached(default, null!);

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void PlayMusic_InvalidConfig_NoOp()
        {
            Assert.DoesNotThrow(() => _service.PlayMusic(default));
        }

        [Test]
        public void PlayLoop_EmptySlot_NoOp()
        {
            _service.PlayLoop("", default);

            Assert.IsFalse(_service.IsLoopPlaying(""));
        }

        [Test]
        public void PlayLoop_InvalidConfig_DoesNotStart()
        {
            _service.PlayLoop("ambient", default);

            Assert.IsFalse(_service.IsLoopPlaying("ambient"));
        }

        [Test]
        public void StopLoop_UnknownSlot_NoOp()
        {
            Assert.DoesNotThrow(() => _service.StopLoop("unknown"));
        }

        [Test]
        public void StopSound_InvalidHandle_NoOp()
        {
            Assert.DoesNotThrow(() => _service.StopSound(SoundHandle.Invalid));
        }

        [Test]
        public void IsLoopPlaying_UnknownSlot_ReturnsFalse()
        {
            Assert.IsFalse(_service.IsLoopPlaying("unknown"));
        }

        [Test]
        public void IsLoopPlaying_EmptySlot_ReturnsFalse()
        {
            Assert.IsFalse(_service.IsLoopPlaying(""));
        }

        [Test]
        public void SetVolume_CustomParam_StoredClamped()
        {
            _service.SetVolume("CommentatorVolume", 1.5f);

            Assert.AreEqual(1f, _service.GetVolume("CommentatorVolume"));
        }

        [Test]
        public void GetVolume_UnknownParam_ReturnsFullVolume()
        {
            Assert.AreEqual(1f, _service.GetVolume("Unknown"));
        }

        [Test]
        public void GetVolume_NeverSetButExposedOnMixer_ReturnsMixerValue()
        {
            var mixer = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioMixer>(TestMixerPath);
            Assume.That(mixer, Is.Not.Null, "the test mixer asset must load");
            SetBackingField(_config, "Mixer", mixer);
            using var service = new UnityAudioService(_config);

            float volume = service.GetVolume("HalfVolume");

            Assert.AreEqual(0.5f, volume, 1e-3f);
        }

        [Test]
        public void GetVolume_NotExposedOnMixer_ReturnsFullVolume()
        {
            SetBackingField(_config, "Mixer", UnityEditor.AssetDatabase.LoadAssetAtPath<AudioMixer>(TestMixerPath));
            using var service = new UnityAudioService(_config);

            Assert.AreEqual(1f, service.GetVolume("Unknown"));
        }

        [Test]
        public void StopMusic_NoMusicPlaying_NoOp()
        {
            Assert.DoesNotThrow(() => _service.StopMusic());
        }

        [Test]
        public void TransitionToSnapshot_NoMixer_NoOp()
        {
            Assert.DoesNotThrow(() => _service.TransitionToSnapshot("Any", 0.5f));
        }

        [Test]
        public void Dispose_CalledOnce_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.Dispose());
            _service = null!;
        }

        [Test]
        public void SetVolume_BelowZero_ClampedToZero()
        {
            _service.SetVolume("MasterVolume", -0.5f);

            Assert.AreEqual(0f, _service.GetVolume("MasterVolume"));
        }

        [Test]
        public void SetVolume_MidRange_StoredExactly()
        {
            _service.SetVolume("MusicVolume", 0.42f);

            Assert.AreEqual(0.42f, _service.GetVolume("MusicVolume"));
        }

        [Test]
        public void StopAllSFX_TwoSoundsPlaying_ReleasesBoth()
        {
            var sound = MakeValidSound();
            _service.PlaySFX(sound);
            _service.PlaySFXAtPoint(sound, Vector3.one);

            _service.StopAllSFX();

            Assert.AreEqual(0, GetField<System.Collections.ICollection>(_service, "_activeSources").Count);
            Assert.AreEqual(0, GetField<System.Collections.ICollection>(_service, "_handleSources").Count);
        }

        [Test]
        public void StopAllSFX_WithFadeOut_ReleasesHandlesAtOnce()
        {
            var sound = MakeValidSound();
            _service.PlaySFX(sound);

            _service.StopAllSFX(fadeOut: 1f);

            Assert.AreEqual(0, GetField<System.Collections.ICollection>(_service, "_handleSources").Count);
        }

        [Test]
        public void SetLoopVolume_LiveLoop_AppliesImmediately()
        {
            _service.PlayLoop("crowd", MakeValidSound(), volumeScale: 0.5f);

            _service.SetLoopVolume("crowd", 0.9f);

            Assert.AreEqual(0.9f, LoopSource("crowd").volume, 1e-5f);
        }

        [Test]
        public void SetLoopPitch_LiveLoop_AppliesImmediately()
        {
            _service.PlayLoop("wind", MakeValidSound());

            _service.SetLoopPitch("wind", 1.5f);

            Assert.AreEqual(1.5f, LoopSource("wind").pitch, 1e-5f);
        }

        [Test]
        public void SetLoopVolume_LoopFadingOut_DoesNotRevive()
        {
            _service.PlayLoop("crowd", MakeValidSound(), volumeScale: 0.5f);
            _service.StopLoop("crowd", fadeOut: 1f);

            _service.SetLoopVolume("crowd", 1f);

            Assert.LessOrEqual(LoopSource("crowd").volume, 0.5f);
        }

        [Test]
        public void SetLoopVolume_StoppedLoop_NoOp()
        {
            _service.PlayLoop("crowd", MakeValidSound(), volumeScale: 0.5f);
            var source = LoopSource("crowd");
            _service.StopLoop("crowd");

            _service.SetLoopVolume("crowd", 1f);

            Assert.AreEqual(0.5f, source.volume, 1e-5f);
        }

        [Test]
        public void StopLoop_NoFade_FreesSlot()
        {
            _service.PlayLoop("crowd", MakeValidSound());

            _service.StopLoop("crowd");

            Assert.IsFalse(LoopSlots(_service).ContainsKey("crowd"));
        }

        [Test]
        public void PlayLoop_AfterOtherSlotStopped_ReusesItsSource()
        {
            _service.PlayLoop("piston-1", MakeValidSound());
            var first = LoopSource("piston-1");
            _service.StopLoop("piston-1");

            _service.PlayLoop("piston-2", MakeValidSound());

            Assert.AreSame(first, LoopSource("piston-2"));
            Assert.AreEqual(1, LoopSourceCount(_service));
        }

        [Test]
        public void PlayLoop_ReusedSourceAfterPitchRamp_StartsClean()
        {
            _service.PlayLoop("piston-1", MakeValidSound());
            _service.SetLoopPitch("piston-1", 2f, duration: 1000f);
            _service.StopLoop("piston-1");

            _service.PlayLoop("piston-2", MakeValidSound());

            Assert.AreEqual(1f, LoopSource("piston-2").pitch, 1e-5f);
            Assert.AreEqual(0, GetField<System.Collections.ICollection>(_service, "_loopPitchWatchers").Count,
                "a ramp of a freed slot must not keep writing to the reused source");
        }

        [Test]
        public void SetLoopPitch_UnknownSlot_NoOp()
        {
            Assert.DoesNotThrow(() => _service.SetLoopPitch("unknown", 2f, 0.5f));
        }

        [Test]
        public void PlaySFXAtPoint_Config3DSettings_AppliedToSource()
        {
            SetBackingField(_config, "Rolloff", AudioRolloffMode.Linear);
            SetBackingField(_config, "MinDistance", 5f);
            SetBackingField(_config, "MaxDistance", 30f);
            SetBackingField(_config, "DopplerLevel", 0f);
            using var service = new UnityAudioService(_config);

            service.PlaySFXAtPoint(MakeValidSound(), Vector3.zero);

            var source = (AudioSource)GetField<System.Collections.IEnumerable>(service, "_activeSources")
                .Cast<object>().Single();
            Assert.AreEqual(AudioRolloffMode.Linear, source.rolloffMode);
            Assert.AreEqual(5f, source.minDistance);
            Assert.AreEqual(30f, source.maxDistance);
            Assert.AreEqual(0f, source.dopplerLevel);
        }

        [Test]
        public void PlaySFX_PoolFullIncomingHigherPriority_EvictsLowestPriority()
        {
            using var service = ServiceWithPool(2);
            var low = MakeSound(NewClip(), priority: 0);
            var high = MakeSound(NewClip(), priority: 10);
            var higher = MakeSound(NewClip(), priority: 20);
            service.PlaySFX(high);
            service.PlaySFX(low);

            var handle = service.PlaySFX(higher);

            Assert.IsTrue(handle.IsValid);
            CollectionAssert.AreEquivalent(new[] { high.Resource, higher.Resource }, PlayingResources(service));
        }

        [Test]
        public void PlaySFX_PoolFullIncomingLowerPriority_ReturnsInvalidAndKeepsPlaying()
        {
            using var service = ServiceWithPool(2);
            var bumper = MakeSound(NewClip(), priority: 10);
            var line = MakeSound(NewClip(), priority: 5);
            service.PlaySFX(bumper);
            service.PlaySFX(line);

            var handle = service.PlaySFX(MakeSound(NewClip(), priority: 0));

            Assert.IsFalse(handle.IsValid);
            CollectionAssert.AreEquivalent(new[] { bumper.Resource, line.Resource }, PlayingResources(service));
        }

        [Test]
        public void PlaySFX_PoolFullEqualPriority_EvictsOldest()
        {
            using var service = ServiceWithPool(2);
            var first = MakeSound(NewClip());
            var second = MakeSound(NewClip());
            var third = MakeSound(NewClip());
            service.PlaySFX(first);
            service.PlaySFX(second);

            var handle = service.PlaySFX(third);

            Assert.IsTrue(handle.IsValid);
            CollectionAssert.AreEqual(new[] { second.Resource, third.Resource }, PlayingResources(service));
        }

        [Test]
        public void PlaySFX_SeveralLowestPriority_EvictsOldestOfThem()
        {
            using var service = ServiceWithPool(3);
            var olderHit = MakeSound(NewClip(), priority: 0);
            var bumper = MakeSound(NewClip(), priority: 10);
            var newerHit = MakeSound(NewClip(), priority: 0);
            service.PlaySFX(bumper);
            service.PlaySFX(olderHit);
            service.PlaySFX(newerHit);

            service.PlaySFX(MakeSound(NewClip(), priority: 0));

            CollectionAssert.DoesNotContain(PlayingResources(service), olderHit.Resource);
            CollectionAssert.Contains(PlayingResources(service), bumper.Resource);
            CollectionAssert.Contains(PlayingResources(service), newerHit.Resource);
        }

        [Test]
        public void PlaySFX_EvictedSound_FreesInstanceSlotAndHandle()
        {
            using var service = ServiceWithPool(1);
            var hit = MakeSound(NewClip(), maxInstances: 1);
            var bumper = MakeSound(NewClip(), priority: 5);
            var evicted = service.PlaySFX(hit);
            service.PlaySFX(bumper);

            service.StopSound(evicted);

            var counts = GetField<System.Collections.Generic.Dictionary<AudioResource, int>>(service, "_instanceCounts");
            Assert.IsFalse(counts.ContainsKey(hit.Resource), "an evicted copy must not hold its sound's instance slot");
            CollectionAssert.AreEqual(new[] { bumper.Resource }, PlayingResources(service));
        }

        [Test]
        public void PlaySFX_MaxInstancesReached_ReturnsInvalid()
        {
            var hit = MakeSound(NewClip(), maxInstances: 2);
            _service.PlaySFX(hit);
            _service.PlaySFX(hit);

            var third = _service.PlaySFX(hit);

            Assert.IsFalse(third.IsValid);
            Assert.AreEqual(2, PlayingResources(_service).Length);
        }

        [Test]
        public void PlaySFX_MaxInstancesAfterStopSound_PlaysAgain()
        {
            var hit = MakeSound(NewClip(), maxInstances: 1);
            var first = _service.PlaySFX(hit);
            _service.StopSound(first);

            var second = _service.PlaySFX(hit);

            Assert.IsTrue(second.IsValid);
        }

        [Test]
        public void PlaySFX_MaxInstances_CountsAtPointAndAttached()
        {
            var hit = MakeSound(NewClip(), maxInstances: 2);
            var follow = new GameObject("follow");
            try
            {
                _service.PlaySFXAtPoint(hit, Vector3.zero);
                _service.PlaySFXAttached(hit, follow.transform);

                var third = _service.PlaySFX(hit);

                Assert.IsFalse(third.IsValid);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(follow);
            }
        }

        [Test]
        public void PlaySFX_FadingOutCopy_NotCountedAsInstance()
        {
            var hit = MakeSound(NewClip(), maxInstances: 1);
            var first = _service.PlaySFX(hit);
            _service.StopSound(first, fadeOut: 10f);

            var second = _service.PlaySFX(hit);

            Assert.IsTrue(second.IsValid);
        }

        [Test]
        public void PlaySFX_DifferentResources_LimitedIndependently()
        {
            var hit = MakeSound(NewClip(), maxInstances: 1);
            var splash = MakeSound(NewClip(), maxInstances: 1);
            _service.PlaySFX(hit);

            var handle = _service.PlaySFX(splash);

            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void PlaySFX_WithinMinInterval_ReturnsInvalid()
        {
            var clock = UseClock(_service);
            var hit = MakeSound(NewClip(), minInterval: 0.05f);
            _service.PlaySFX(hit);
            clock.Now += 0.04;

            var second = _service.PlaySFX(hit);

            Assert.IsFalse(second.IsValid);
        }

        [Test]
        public void PlaySFX_AfterMinInterval_Plays()
        {
            var clock = UseClock(_service);
            var hit = MakeSound(NewClip(), minInterval: 0.05f);
            _service.PlaySFX(hit);
            clock.Now += 0.04;
            _service.PlaySFX(hit);
            clock.Now += 0.02;

            var third = _service.PlaySFX(hit);

            Assert.IsTrue(third.IsValid, "a dropped start must not restart the interval");
        }

        [Test]
        public void PlayLoopAtPoint_ValidSound_PlaysIn3DAtPosition()
        {
            SetBackingField(_config, "Rolloff", AudioRolloffMode.Linear);
            SetBackingField(_config, "MinDistance", 5f);
            SetBackingField(_config, "MaxDistance", 40f);
            using var service = new UnityAudioService(_config);
            var position = new Vector3(3f, 1f, -7f);

            service.PlayLoopAtPoint("spinner", MakeValidSound(), position);

            var source = LoopSource(service, "spinner");
            Assert.AreEqual(1f, source.spatialBlend);
            Assert.AreEqual(position, source.transform.position);
            Assert.AreEqual(AudioRolloffMode.Linear, source.rolloffMode);
            Assert.AreEqual(5f, source.minDistance);
            Assert.AreEqual(40f, source.maxDistance);
            Assert.IsTrue(source.loop);
        }

        [Test]
        public void PlayLoopAttached_ValidSound_StartsAtTargetIn3D()
        {
            var piston = NewTarget(new Vector3(10f, 2f, 0f));

            _service.PlayLoopAttached("motor", MakeValidSound(), piston);

            var source = LoopSource("motor");
            Assert.AreEqual(1f, source.spatialBlend);
            Assert.AreEqual(piston.position, source.transform.position);
        }

        [Test]
        public void PlayLoopAttached_TargetMoves_SourceFollows()
        {
            var piston = NewTarget(Vector3.zero);
            _service.PlayLoopAttached("motor", MakeValidSound(), piston);
            piston.position = new Vector3(0f, 0f, 4f);

            StepLoopFollows(_service);

            Assert.AreEqual(piston.position, LoopSource("motor").transform.position);
        }

        [Test]
        public void PlayLoopAttached_TargetDestroyed_SlotFadesOut()
        {
            var piston = NewTarget(new Vector3(0f, 0f, 6f));
            _service.PlayLoopAttached("motor", MakeValidSound(), piston, volumeScale: 0.5f);
            UnityEngine.Object.DestroyImmediate(piston.gameObject);

            StepLoopFollows(_service);
            _service.SetLoopVolume("motor", 1f);

            var source = LoopSource("motor");
            Assert.IsNotNull(source.resource, "the loop fades out instead of cutting off");
            Assert.LessOrEqual(source.volume, 0.5f, "a fading loop must not be revived");
            Assert.AreEqual(new Vector3(0f, 0f, 6f), source.transform.position);
            Assert.AreEqual(0, LoopFollows(_service).Count);
        }

        [Test]
        public void PlayLoopAttached_TargetDestroyedNoFade_StopsAtOnce()
        {
            SetBackingField(_config, "LostTargetFadeOut", 0f);
            using var service = new UnityAudioService(_config);
            var piston = NewTarget(Vector3.zero);
            service.PlayLoopAttached("motor", MakeValidSound(), piston);
            var source = LoopSource(service, "motor");
            UnityEngine.Object.DestroyImmediate(piston.gameObject);

            StepLoopFollows(service);

            Assert.IsNull(source.resource);
            Assert.IsFalse(service.IsLoopPlaying("motor"));
        }

        [Test]
        public void PlayLoopAttached_NullTarget_LeavesSlotUntouched()
        {
            var wind = MakeValidSound();
            _service.PlayLoop("wind", wind, volumeScale: 0.3f);

            _service.PlayLoopAttached("wind", MakeValidSound(), null!);

            var source = LoopSource("wind");
            Assert.AreSame(wind.Resource, source.resource);
            Assert.AreEqual(0f, source.spatialBlend);
            Assert.AreEqual(0.3f, source.volume, 1e-5f);
        }

        [Test]
        public void PlayLoop_OnAttachedSlot_Becomes2DAndStopsFollowing()
        {
            var piston = NewTarget(Vector3.zero);
            _service.PlayLoopAttached("motor", MakeValidSound(), piston);

            _service.PlayLoop("motor", MakeValidSound());

            Assert.AreEqual(0f, LoopSource("motor").spatialBlend);
            Assert.AreEqual(0, LoopFollows(_service).Count);
        }

        [Test]
        public void PlayLoopAttached_LiveLoop_SetLoopVolumeAndPitchApply()
        {
            _service.PlayLoopAttached("motor", MakeValidSound(), NewTarget(Vector3.zero), volumeScale: 0.5f);

            _service.SetLoopVolume("motor", 0.8f);
            _service.SetLoopPitch("motor", 1.4f);

            var source = LoopSource("motor");
            Assert.AreEqual(0.8f, source.volume, 1e-5f);
            Assert.AreEqual(1.4f, source.pitch, 1e-5f);
        }

        [Test]
        public void StopLoop_AttachedFadingOutTargetDestroyed_KeepsItsFade()
        {
            var piston = NewTarget(Vector3.zero);
            _service.PlayLoopAttached("motor", MakeValidSound(), piston);
            _service.StopLoop("motor", fadeOut: 1f);
            var fade = LoopWatchers(_service)["motor"];
            UnityEngine.Object.DestroyImmediate(piston.gameObject);

            StepLoopFollows(_service);

            Assert.AreSame(fade, LoopWatchers(_service)["motor"], "the fade must not restart");
            Assert.IsNotNull(LoopSource("motor").resource);
            Assert.AreEqual(0, LoopFollows(_service).Count);
        }

        [Test]
        public void StopLoop_AttachedNoFade_StopsFollowing()
        {
            _service.PlayLoopAttached("motor", MakeValidSound(), NewTarget(Vector3.zero));

            _service.StopLoop("motor");

            Assert.AreEqual(0, LoopFollows(_service).Count);
        }

        [Test]
        public void PlaySFX_PlaysOnPause_SetsIgnoreListenerPause()
        {
            _service.PlaySFX(MakeSound(NewClip(), playsOnPause: true));

            Assert.IsTrue(ActiveSource(_service).ignoreListenerPause);
        }

        [Test]
        public void PlaySFX_AfterPlaysOnPauseSound_ClearsIgnoreListenerPause()
        {
            using var service = ServiceWithPool(1);
            service.StopSound(service.PlaySFX(MakeSound(NewClip(), playsOnPause: true)));

            service.PlaySFX(MakeSound(NewClip()));

            Assert.IsFalse(ActiveSource(service).ignoreListenerPause, "a reused source must not keep playing on pause");
        }

        [Test]
        public void PlayLoop_PlaysOnPause_SetsIgnoreListenerPause()
        {
            _service.PlayLoop("jingle", MakeSound(NewClip(), playsOnPause: true));

            Assert.IsTrue(LoopSource("jingle").ignoreListenerPause);
        }

        [Test]
        public void PlayLoop_OrdinarySoundAfterPlaysOnPause_ClearsIgnoreListenerPause()
        {
            _service.PlayLoop("jingle", MakeSound(NewClip(), playsOnPause: true));

            _service.PlayLoop("jingle", MakeSound(NewClip()));

            Assert.IsFalse(LoopSource("jingle").ignoreListenerPause);
        }

        [Test]
        public void PlayMusic_SwitchedMidCrossfade_OldTrackKeepsFading()
        {
            var opening = MakeMusic();
            _service.PlayMusic(opening, crossfadeDuration: 0f);
            _service.PlayMusic(MakeMusic(), crossfadeDuration: 1000f);
            float openingVolume = MusicSourceOf(_service, opening).volume;

            _service.PlayMusic(MakeMusic(), crossfadeDuration: 1000f);

            var source = MusicSourceOf(_service, opening);
            Assert.IsNotNull(source, "the fading track must not lose its source");
            Assert.AreEqual(openingVolume, source!.volume, 0.01f);
        }

        [Test]
        public void PlayMusic_SwitchedMidCrossfade_NewTrackFadesIn()
        {
            _service.PlayMusic(MakeMusic(), crossfadeDuration: 0f);
            _service.PlayMusic(MakeMusic(), crossfadeDuration: 1000f);
            var next = MakeMusic();

            _service.PlayMusic(next, crossfadeDuration: 1000f);

            var source = MusicSourceOf(_service, next);
            Assert.IsNotNull(source);
            Assert.IsTrue(source!.isPlaying);
            Assert.Less(source.volume, 0.01f);
        }

        [Test]
        public void PlayMusic_TrackAlreadyPlaying_KeepsPlaying()
        {
            var run = MakeMusic();
            _service.PlayMusic(run, crossfadeDuration: 0f);
            var source = MusicSourceOf(_service, run);

            _service.PlayMusic(run, crossfadeDuration: 1000f);

            Assert.AreEqual(1, MusicSources(_service).Count(music => music.resource == run.Resource),
                "the playing track must not start again on another source");
            Assert.AreSame(source, MusicSourceOf(_service, run));
            Assert.AreEqual(1f, source!.volume, 1e-5f);
        }

        [Test]
        public void PlayMusic_TrackFadingOut_FadesBackWithoutRestart()
        {
            var run = MakeMusic();
            _service.PlayMusic(run, crossfadeDuration: 0f);
            _service.PlayMusic(MakeMusic(), crossfadeDuration: 1000f);
            var source = MusicSourceOf(_service, run);

            _service.PlayMusic(run, crossfadeDuration: 1000f);

            Assert.AreEqual(1, MusicSources(_service).Count(music => music.resource == run.Resource));
            Assert.AreSame(source, MusicSourceOf(_service, run));
            Assert.Greater(source!.volume, 0.9f, "the track goes on from where its fade was, not from silence");
        }

        [Test]
        public void PlayMusic_SameTrackAfterStopMusic_PlaysAgain()
        {
            var run = MakeMusic();
            _service.PlayMusic(run, crossfadeDuration: 0f);
            _service.StopMusic();

            _service.PlayMusic(run, crossfadeDuration: 0f);

            var source = MusicSourceOf(_service, run);
            Assert.IsNotNull(source);
            Assert.IsTrue(source!.isPlaying);
            Assert.AreEqual(1f, source.volume, 1e-5f);
        }

        [UnityTest]
        public IEnumerator StopSound_AttachedWithFadeOut_KeepsFollowing()
        {
            var target = NewTarget(Vector3.zero);
            var handle = _service.PlaySFXAttached(MakeValidSound(), target);
            var source = HandleSource(_service, handle);
            _service.StopSound(handle, fadeOut: 1000f);

            target.position = new Vector3(0f, 0f, 5f);
            for (int i = 0; i < 5; i++) yield return null;

            Assert.AreEqual(target.position, source.transform.position);
        }

        [Test]
        public void PlayMusic_PlaysOnPause_SetsIgnoreListenerPause()
        {
            _service.PlayMusic(MakeMusic(playsOnPause: true), crossfadeDuration: 0f);

            Assert.IsTrue(MusicSources(_service).Any(source => source.resource != null && source.ignoreListenerPause));
        }

        private static AudioSource ActiveSource(UnityAudioService service) =>
            GetField<System.Collections.IEnumerable>(service, "_activeSources").Cast<AudioSource>().Single();

        private static AudioSource[] MusicSources(UnityAudioService service) =>
            Root(service).GetComponentsInChildren<AudioSource>()
                .Where(source => source.name.StartsWith("Music_", StringComparison.Ordinal)).ToArray();

        private static AudioSource? MusicSourceOf(UnityAudioService service, MusicConfig music) =>
            MusicSources(service).SingleOrDefault(source => source.resource == music.Resource);

        private static AudioSource[] SfxSources(UnityAudioService service) =>
            Root(service).GetComponentsInChildren<AudioSource>().Where(source => source.name == "SFX_Source").ToArray();

        private static int LoopSourceCount(UnityAudioService service) =>
            Root(service).GetComponentsInChildren<AudioSource>()
                .Count(source => source.name.StartsWith("Loop_", StringComparison.Ordinal));

        private static GameObject Root(UnityAudioService service) => GetField<GameObject>(service, "_root");

        private static AudioSource HandleSource(UnityAudioService service, SoundHandle handle) =>
            GetField<System.Collections.Generic.Dictionary<long, AudioSource>>(service, "_handleSources")[
                (long)typeof(SoundHandle).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handle)];

        private Transform NewTarget(Vector3 position)
        {
            var target = new GameObject("Target");
            target.transform.position = position;
            _targets.Add(target);
            return target.transform;
        }

        private static void StepLoopFollows(UnityAudioService service) =>
            typeof(UnityAudioService).GetMethod("StepLoopFollows", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(service, null);

        private static System.Collections.ICollection LoopFollows(UnityAudioService service) =>
            GetField<System.Collections.ICollection>(service, "_loopFollows");

        private static System.Collections.Generic.Dictionary<string, System.Threading.CancellationTokenSource> LoopWatchers(
            UnityAudioService service) =>
            GetField<System.Collections.Generic.Dictionary<string, System.Threading.CancellationTokenSource>>(service, "_loopWatchers");

        private UnityAudioService ServiceWithPool(int size)
        {
            SetMaxSfxSources(_config, size);
            return new UnityAudioService(_config);
        }

        private static AudioResource[] PlayingResources(UnityAudioService service) =>
            GetField<System.Collections.IEnumerable>(service, "_activeSources")
                .Cast<AudioSource>().Select(source => source.resource).ToArray();

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

        private static AudioClip NewClip() => AudioClip.Create("test", 1, 1, 44100, false);

        private static SoundConfig MakeSound(AudioClip clip, int priority = 0, int maxInstances = 0, float minInterval = 0f,
            bool playsOnPause = false)
        {
            object boxed = default(SoundConfig);
            SetSoundField(boxed, "_resource", clip);
            SetSoundField(boxed, "_priority", priority);
            SetSoundField(boxed, "_maxInstances", maxInstances);
            SetSoundField(boxed, "_minInterval", minInterval);
            SetSoundField(boxed, "_playsOnPause", playsOnPause);
            return (SoundConfig)boxed;
        }

        private static MusicConfig MakeMusic(bool playsOnPause = false)
        {
            object boxed = default(MusicConfig);
            typeof(MusicConfig).GetField("_resource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(boxed, NewClip());
            typeof(MusicConfig).GetField("_playsOnPause", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(boxed, playsOnPause);
            return (MusicConfig)boxed;
        }

        private static void SetSoundField(object boxed, string name, object value) =>
            typeof(SoundConfig).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(boxed, value);

        private AudioSource LoopSource(string slot) => LoopSource(_service, slot);

        private static AudioSource LoopSource(UnityAudioService service, string slot) => LoopSlots(service)[slot];

        private static System.Collections.Generic.Dictionary<string, AudioSource> LoopSlots(UnityAudioService service) =>
            GetField<System.Collections.Generic.Dictionary<string, AudioSource>>(service, "_loopSources");

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);

        private static void SetBackingField(AudioServiceConfig config, string property, object value) =>
            typeof(AudioServiceConfig)
                .GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(config, value);

        private static SoundConfig MakeValidSound()
        {
            // SoundConfig.IsValid is just `_resource != null`; a runtime-created AudioClip
            // (an AudioResource) is enough to drive the rent/play path without a real asset.
            var clip = AudioClip.Create("test", 1, 1, 44100, false);
            object boxed = default(SoundConfig);
            typeof(SoundConfig).GetField("_resource", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(boxed, clip);
            return (SoundConfig)boxed;
        }

        private static void SetMaxSfxSources(AudioServiceConfig config, int count)
        {
            // MaxSfxSources has a private setter (auto-property backing field) and is read once
            // in the constructor, so set it before building the service under test.
            typeof(AudioServiceConfig)
                .GetField("<MaxSfxSources>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(config, count);
        }
    }
}
