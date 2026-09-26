using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Audio.Tests
{
    [TestFixture]
    public class UnityAudioServiceTests
    {
        private AudioServiceConfig _config = null!;
        private UnityAudioService _service = null!;

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
        public void PlaySFX_PoolEmptyAndAllSourcesFadingOut_GrowsFreshSourceInsteadOfThrowing()
        {
            // Regression: with the single pooled source rented and then pushed into fade-out
            // limbo by StopSound (removed from _activeSources but not yet reclaimed by the fade
            // task), the next RentSource saw an empty pool AND an empty active list and NRE'd on
            // _activeSources.First!. It must instead grow a fresh source so the sound still plays.
            SetMaxSfxSources(_config, 1);
            using var service = new UnityAudioService(_config);
            var sound = MakeValidSound();

            var first = service.PlaySFX(sound);          // rents the only pooled source
            service.StopSound(first, fadeOut: 10f);      // pool + active list now both empty

            SoundHandle second = default;
            Assert.DoesNotThrow(() => second = service.PlaySFX(sound),
                "RentSource must not dereference an empty active list when every source is fading out");
            Assert.IsTrue(second.IsValid, "a fresh source must be created when nothing is available");
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
            _service.StopLoop("crowd");

            _service.SetLoopVolume("crowd", 1f);

            Assert.AreEqual(0.5f, LoopSource("crowd").volume, 1e-5f);
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

        private AudioSource LoopSource(string slot) =>
            GetField<System.Collections.Generic.Dictionary<string, AudioSource>>(_service, "_loopSources")[slot];

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
