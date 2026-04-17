using System;
using Cysharp.Threading.Tasks;
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
        public void SetMasterVolume_BelowZero_ClampedToZero()
        {
            _service.SetMasterVolume(-0.5f);

            Assert.AreEqual(0f, _service.MasterVolume);
        }

        [Test]
        public void SetMasterVolume_AboveOne_ClampedToOne()
        {
            _service.SetMasterVolume(2f);

            Assert.AreEqual(1f, _service.MasterVolume);
        }

        [Test]
        public void SetMusicVolume_MidRange_StoredExactly()
        {
            _service.SetMusicVolume(0.42f);

            Assert.AreEqual(0.42f, _service.MusicVolume);
        }

        [Test]
        public void SetSFXVolume_MidRange_StoredExactly()
        {
            _service.SetSFXVolume(0.37f);

            Assert.AreEqual(0.37f, _service.SFXVolume);
        }

        [Test]
        public void StopMusic_NoMusicPlaying_NoOp()
        {
            Assert.DoesNotThrow(() => _service.StopMusic());
        }

        [Test]
        public void DuckSFX_NoMixer_NoOp()
        {
            Assert.DoesNotThrow(() => _service.DuckSFX(0.3f, 1f));
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
        public void Constructor_WithStorage_HydratesVolumesFromStorage()
        {
            var storage = new InMemoryStorageService();
            storage.SetFloat("audio_master", 0.1f).Forget();
            storage.SetFloat("audio_music", 0.2f).Forget();
            storage.SetFloat("audio_sfx", 0.3f).Forget();

            using var service = new UnityAudioService(_config, storage);

            Assert.AreEqual(0.1f, service.MasterVolume);
            Assert.AreEqual(0.2f, service.MusicVolume);
            Assert.AreEqual(0.3f, service.SFXVolume);
        }

        [Test]
        public void SetMasterVolume_WithStorage_PersistsValue()
        {
            var storage = new InMemoryStorageService();
            using var service = new UnityAudioService(_config, storage);

            service.SetMasterVolume(0.6f);

            Assert.AreEqual(0.6f, storage.GetFloat("audio_master"));
        }

        [Test]
        public void SetMusicVolume_WithStorage_PersistsValue()
        {
            var storage = new InMemoryStorageService();
            using var service = new UnityAudioService(_config, storage);

            service.SetMusicVolume(0.5f);

            Assert.AreEqual(0.5f, storage.GetFloat("audio_music"));
        }

        [Test]
        public void SetSFXVolume_WithStorage_PersistsValue()
        {
            var storage = new InMemoryStorageService();
            using var service = new UnityAudioService(_config, storage);

            service.SetSFXVolume(0.4f);

            Assert.AreEqual(0.4f, storage.GetFloat("audio_sfx"));
        }
    }
}
