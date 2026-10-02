using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rubickanov.Storage.Tests
{
    [TestFixture]
    public class NullStorageServiceTests
    {
        private NullStorageService _service = null!;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _service = new NullStorageService();
        }

        [Test]
        public void GetFloat_ReturnsDefault() => Assert.AreEqual(1.5f, _service.GetFloat("k", 1.5f));

        [Test]
        public void GetInt_ReturnsDefault() => Assert.AreEqual(9, _service.GetInt("k", 9));

        [Test]
        public void GetString_ReturnsDefault() => Assert.AreEqual("def", _service.GetString("k", "def"));

        [Test]
        public void HasKey_AlwaysFalse() => Assert.IsFalse(_service.HasKey("k"));

        [Test]
        public async Task SettersAndClear_CompleteImmediately()
        {
            await _service.SetFloat("a", 1f).AsTask();
            await _service.SetInt("b", 2).AsTask();
            await _service.SetString("c", "x").AsTask();
            await _service.DeleteKey("a").AsTask();
            await _service.Clear().AsTask();

            Assert.Pass();
        }
    }
}
