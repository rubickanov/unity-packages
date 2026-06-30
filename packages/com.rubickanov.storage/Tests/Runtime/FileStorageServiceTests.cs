using System.IO;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rubickanov.Storage.Tests
{
    [TestFixture]
    public class FileStorageServiceTests
    {
        private string _tempDir = null!;
        private string _filePath = null!;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _tempDir = Path.Combine(Path.GetTempPath(), "storage-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _filePath = Path.Combine(_tempDir, "store.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Test]
        public async Task RoundTrip_WritesAndReadsValuesAcrossInstances()
        {
            var first = new FileStorageService(_filePath);
            await first.SetFloat("volume", 0.75f).AsTask();
            await first.SetInt("score", 42).AsTask();
            await first.SetString("name", "player-one").AsTask();

            var second = new FileStorageService(_filePath);

            Assert.AreEqual(0.75f, second.GetFloat("volume"));
            Assert.AreEqual(42, second.GetInt("score"));
            Assert.AreEqual("player-one", second.GetString("name"));
        }

        [Test]
        public async Task RoundTrip_FloatWithFullPrecision_PreservesExactBits()
        {
            // A value whose shortest decimal differs from its default ("G") rendering — persisting
            // with the round-trip ("R") format must read back bit-for-bit, not an approximation.
            const float precise = 0.123456789f;

            var first = new FileStorageService(_filePath);
            await first.SetFloat("precise", precise).AsTask();

            var second = new FileStorageService(_filePath);

            Assert.AreEqual(precise, second.GetFloat("precise"));
        }

        [Test]
        public void Constructor_CorruptJson_BacksUpFileAndStartsEmpty()
        {
            File.WriteAllText(_filePath, "{not valid json", Encoding.UTF8);

            var service = new FileStorageService(_filePath);

            Assert.IsFalse(service.HasKey("anything"));
            Assert.IsFalse(File.Exists(_filePath), "Corrupt file should have been renamed.");
            var bak = Directory.GetFiles(_tempDir, "store.json.corrupt-*.bak");
            Assert.AreEqual(1, bak.Length, "Exactly one .bak file expected.");
        }

        [Test]
        public async Task RoundTrip_EscapedCharacters_PreservedAcrossInstances()
        {
            var first = new FileStorageService(_filePath);
            await first.SetString("key\"with\\special\nchars\r\t", "value\"with\\special\nchars\r\t").AsTask();

            var second = new FileStorageService(_filePath);

            Assert.AreEqual(
                "value\"with\\special\nchars\r\t",
                second.GetString("key\"with\\special\nchars\r\t"));
        }

        [Test]
        public async Task ConcurrentSaves_LastValueLandsOnDisk()
        {
            var service = new FileStorageService(_filePath);

            service.SetString("a", "1").Forget();
            service.SetString("b", "2").Forget();
            await service.SetString("c", "3").AsTask();

            var reloaded = new FileStorageService(_filePath);
            Assert.AreEqual("1", reloaded.GetString("a"));
            Assert.AreEqual("2", reloaded.GetString("b"));
            Assert.AreEqual("3", reloaded.GetString("c"));
        }

        [Test]
        public async Task Clear_WipesAllKeysOnDisk()
        {
            var service = new FileStorageService(_filePath);
            await service.SetString("a", "1").AsTask();
            await service.SetString("b", "2").AsTask();

            await service.Clear().AsTask();

            var reloaded = new FileStorageService(_filePath);
            Assert.IsFalse(reloaded.HasKey("a"));
            Assert.IsFalse(reloaded.HasKey("b"));
        }
    }
}
