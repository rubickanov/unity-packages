using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rubickanov.Storage.Tests
{
    [TestFixture]
    public class EncryptedStorageServiceTests
    {
        private InMemoryStorageService _inner = null!;
        private EncryptedStorageService _service = null!;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _inner = new InMemoryStorageService();
            _service = new EncryptedStorageService(_inner, "test-passphrase");
        }

        [Test]
        public async Task RoundTrip_FloatIntString_PreservedThroughEncryption()
        {
            await _service.SetFloat("f", 3.14f).AsTask();
            await _service.SetInt("i", 777).AsTask();
            await _service.SetString("s", "hello").AsTask();

            Assert.AreEqual(3.14f, _service.GetFloat("f"));
            Assert.AreEqual(777, _service.GetInt("i"));
            Assert.AreEqual("hello", _service.GetString("s"));
        }

        [Test]
        public async Task GetString_WrongPassphrase_ReturnsDefault()
        {
            await _service.SetString("secret", "red-wolf").AsTask();

            var wrong = new EncryptedStorageService(_inner, "different-passphrase");
            Assert.AreEqual("fallback", wrong.GetString("secret", "fallback"));
        }

        [Test]
        public async Task SetString_SameValueTwice_ProducesDifferentCiphertexts()
        {
            await _service.SetString("k1", "same").AsTask();
            var cipher1 = _inner.GetString("k1");
            await _service.SetString("k2", "same").AsTask();
            var cipher2 = _inner.GetString("k2");

            Assert.AreNotEqual(cipher1, cipher2, "AES-CBC with random IV must produce distinct ciphertexts for identical plaintexts.");
        }

        [Test]
        public async Task Clear_DelegatesToInner()
        {
            await _service.SetString("a", "1").AsTask();

            await _service.Clear().AsTask();

            Assert.IsFalse(_inner.HasKey("a"));
        }
    }
}
