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
        public void PlayLoopAtPoint_AnySlot_DoesNotPlay()
        {
            _service.PlayLoopAtPoint("spinner", default, Vector3.zero);

            Assert.IsFalse(_service.IsLoopPlaying("spinner"));
        }

        [Test]
        public void PlayLoopAttached_NullTarget_DoesNotPlay()
        {
            _service.PlayLoopAttached("motor", default, null!);

            Assert.IsFalse(_service.IsLoopPlaying("motor"));
        }

        [Test]
        public void SetVolume_ThenGet_ReturnsSetValue()
        {
            _service.SetVolume("CommentatorVolume", 0.4f);

            Assert.AreEqual(0.4f, _service.GetVolume("CommentatorVolume"));
        }

        [Test]
        public void GetVolume_NeverSet_ReturnsOne()
        {
            Assert.AreEqual(1f, _service.GetVolume("MasterVolume"));
        }
    }
}
