using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class InputExtensionsTests
    {
        private struct Payload
        {
            public int Value;
        }

        private struct OtherPayload
        {
            public float X;
        }

        [Test]
        public void Get_MissingKey_ReturnsDefault()
        {
            var ext = new InputExtensions();

            var value = ext.Get<Payload>();

            Assert.AreEqual(default(Payload), value);
        }

        [Test]
        public void Set_ThenGet_ReturnsStoredValue()
        {
            var ext = new InputExtensions();

            ext.Set(new Payload { Value = 42 });

            Assert.AreEqual(42, ext.Get<Payload>().Value);
        }

        [Test]
        public void Set_SameTypeTwice_OverwritesValue()
        {
            var ext = new InputExtensions();
            ext.Set(new Payload { Value = 1 });

            ext.Set(new Payload { Value = 2 });

            Assert.AreEqual(2, ext.Get<Payload>().Value);
        }

        [Test]
        public void TryGet_MissingKey_ReturnsFalseAndDefault()
        {
            var ext = new InputExtensions();

            bool found = ext.TryGet<Payload>(out var value);

            Assert.IsFalse(found);
            Assert.AreEqual(default(Payload), value);
        }

        [Test]
        public void TryGet_PresentKey_ReturnsTrueAndValue()
        {
            var ext = new InputExtensions();
            ext.Set(new Payload { Value = 7 });

            bool found = ext.TryGet<Payload>(out var value);

            Assert.IsTrue(found);
            Assert.AreEqual(7, value.Value);
        }

        [Test]
        public void Has_PresentKey_ReturnsTrue()
        {
            var ext = new InputExtensions();
            ext.Set(new Payload { Value = 1 });

            Assert.IsTrue(ext.Has<Payload>());
            Assert.IsFalse(ext.Has<OtherPayload>());
        }

        [Test]
        public void Remove_PresentKey_ReturnsTrueAndErases()
        {
            var ext = new InputExtensions();
            ext.Set(new Payload { Value = 1 });

            bool removed = ext.Remove<Payload>();

            Assert.IsTrue(removed);
            Assert.IsFalse(ext.Has<Payload>());
        }

        [Test]
        public void Remove_MissingKey_ReturnsFalse()
        {
            var ext = new InputExtensions();

            Assert.IsFalse(ext.Remove<Payload>());
        }

        [Test]
        public void Clear_AllEntries_Empties()
        {
            var ext = new InputExtensions();
            ext.Set(new Payload { Value = 1 });
            ext.Set(new OtherPayload { X = 2f });

            ext.Clear();

            Assert.IsFalse(ext.Has<Payload>());
            Assert.IsFalse(ext.Has<OtherPayload>());
        }

        [Test]
        public void DifferentTypes_StoredIndependently()
        {
            var ext = new InputExtensions();
            ext.Set(new Payload { Value = 1 });
            ext.Set(new OtherPayload { X = 3.5f });

            Assert.AreEqual(1, ext.Get<Payload>().Value);
            Assert.AreEqual(3.5f, ext.Get<OtherPayload>().X);
        }
    }
}
