using System;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rubickanov.Storage.Tests
{
    [TestFixture]
    public class ConstructorValidationTests
    {
        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [Test]
        public void FileStorageService_NullPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new FileStorageService(null!));
        }

        [Test]
        public void FileStorageService_EmptyPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new FileStorageService(""));
        }

        [Test]
        public void FileStorageService_WhitespacePath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new FileStorageService("   "));
        }

        [Test]
        public void EncryptedStorageService_NullInner_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new EncryptedStorageService(null!, "passphrase"));
        }

        [Test]
        public void EncryptedStorageService_NullPassphrase_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => new EncryptedStorageService(new InMemoryStorageService(), null!));
        }

        [Test]
        public void EncryptedStorageService_EmptyPassphrase_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => new EncryptedStorageService(new InMemoryStorageService(), ""));
        }

        [Test]
        public void PrefixedStorageService_NullInner_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PrefixedStorageService(null!, "p."));
        }

        [Test]
        public void PrefixedStorageService_EmptyPrefix_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => new PrefixedStorageService(new InMemoryStorageService(), ""));
        }
    }
}
