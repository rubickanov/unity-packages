using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rubickanov.Storage.Tests
{
    [TestFixture]
    public class PrefixedStorageServiceTests
    {
        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [Test]
        public async Task TwoPrefixes_SameInner_KeysDoNotCollide()
        {
            var inner = new InMemoryStorageService();
            var settings = new PrefixedStorageService(inner, "settings.");
            var save = new PrefixedStorageService(inner, "save.");

            await settings.SetString("volume", "0.5").AsTask();
            await save.SetString("volume", "ignored").AsTask();

            Assert.AreEqual("0.5", settings.GetString("volume"));
            Assert.AreEqual("ignored", save.GetString("volume"));
            Assert.IsTrue(inner.HasKey("settings.volume"));
            Assert.IsTrue(inner.HasKey("save.volume"));
        }

        [Test]
        public async Task DeleteKey_OnlyRemovesPrefixedKey()
        {
            var inner = new InMemoryStorageService();
            var settings = new PrefixedStorageService(inner, "settings.");
            await inner.SetString("settings.a", "x").AsTask();
            await inner.SetString("other.a", "y").AsTask();

            await settings.DeleteKey("a").AsTask();

            Assert.IsFalse(inner.HasKey("settings.a"));
            Assert.IsTrue(inner.HasKey("other.a"));
        }

        [Test]
        public void Clear_Throws_NotSupported()
        {
            var service = new PrefixedStorageService(new InMemoryStorageService(), "p.");

            Assert.Throws<NotSupportedException>(() => service.Clear());
        }
    }
}
