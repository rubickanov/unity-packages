using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Audio.Tests
{
    [TestFixture]
    public class NullAudioServiceTests
    {
        private NullAudioService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _service = new NullAudioService();
        }

        [Test]
        public void PlaySFX_AlwaysReturnsInvalidHandle()
        {
            var handle = _service.PlaySFX(default);

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void PlaySFXAtPoint_AlwaysReturnsInvalidHandle()
        {
            var handle = _service.PlaySFXAtPoint(default, Vector3.zero);

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void PlaySFXAttached_AlwaysReturnsInvalidHandle()
        {
            var handle = _service.PlaySFXAttached(default, null!);

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void IsLoopPlaying_AnySlot_ReturnsFalse()
        {
            _service.PlayLoop("ambient", default);

            Assert.IsFalse(_service.IsLoopPlaying("ambient"));
        }

        [Test]
        public void SetMasterVolume_ThenGet_ReturnsSetValue()
        {
            _service.SetMasterVolume(0.25f);

            Assert.AreEqual(0.25f, _service.MasterVolume);
        }

        [Test]
        public void SetMusicVolume_ThenGet_ReturnsSetValue()
        {
            _service.SetMusicVolume(0.5f);

            Assert.AreEqual(0.5f, _service.MusicVolume);
        }

        [Test]
        public void SetSFXVolume_ThenGet_ReturnsSetValue()
        {
            _service.SetSFXVolume(0.75f);

            Assert.AreEqual(0.75f, _service.SFXVolume);
        }

        [Test]
        public void Volumes_InitialValues_AreOne()
        {
            Assert.AreEqual(1f, _service.MasterVolume);
            Assert.AreEqual(1f, _service.MusicVolume);
            Assert.AreEqual(1f, _service.SFXVolume);
        }
    }
}
