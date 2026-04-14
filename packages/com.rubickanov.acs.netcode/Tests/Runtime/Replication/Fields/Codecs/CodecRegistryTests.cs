using System;
using NUnit.Framework;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class CodecRegistryTests
    {
        // ---- Default (None) returns RawCodec<T> ---------------------------------

        [Test]
        public void Resolve_NoneOnInt_ReturnsRawCodec()
        {
            var codec = CodecRegistry.Resolve(typeof(int), QuantizationMode.None);
            Assert.IsInstanceOf<RawCodec<int>>(codec);
        }

        [Test]
        public void Resolve_NoneOnVector3_ReturnsRawCodec()
        {
            var codec = CodecRegistry.Resolve(typeof(Vector3), QuantizationMode.None);
            Assert.IsInstanceOf<RawCodec<Vector3>>(codec);
        }

        [Test]
        public void Resolve_NoneOnSameTypeTwice_ReturnsSameInstance()
        {
            // Singleton contract: per-T raw codec is cached so binding allocations don't
            // produce a fresh codec object each time. Bindings are created once per spawn,
            // but the cache also catches accidental codec churn under refactors.
            var first = CodecRegistry.Resolve(typeof(Vector3), QuantizationMode.None);
            var second = CodecRegistry.Resolve(typeof(Vector3), QuantizationMode.None);
            Assert.AreSame(first, second);
        }

        // ---- Quantizing modes return matching codec -----------------------------

        [Test]
        public void Resolve_HalfPrecisionOnFloat_ReturnsFloatHalfCodec()
        {
            var codec = CodecRegistry.Resolve(typeof(float), QuantizationMode.HalfPrecision);
            Assert.IsInstanceOf<FloatHalfCodec>(codec);
        }

        [Test]
        public void Resolve_HalfPrecisionOnVector2_ReturnsVector2HalfCodec()
        {
            var codec = CodecRegistry.Resolve(typeof(Vector2), QuantizationMode.HalfPrecision);
            Assert.IsInstanceOf<Vector2HalfCodec>(codec);
        }

        [Test]
        public void Resolve_HalfPrecisionOnVector3_ReturnsVector3HalfCodec()
        {
            var codec = CodecRegistry.Resolve(typeof(Vector3), QuantizationMode.HalfPrecision);
            Assert.IsInstanceOf<Vector3HalfCodec>(codec);
        }

        [Test]
        public void Resolve_HalfPrecisionOnVector4_ReturnsVector4HalfCodec()
        {
            var codec = CodecRegistry.Resolve(typeof(Vector4), QuantizationMode.HalfPrecision);
            Assert.IsInstanceOf<Vector4HalfCodec>(codec);
        }

        [Test]
        public void Resolve_SmallestThreeOnQuaternion_ReturnsQuaternionSmallestThreeCodec()
        {
            var codec = CodecRegistry.Resolve(typeof(Quaternion), QuantizationMode.SmallestThree);
            Assert.IsInstanceOf<QuaternionSmallestThreeCodec>(codec);
        }

        // ---- Invalid combinations throw -----------------------------------------

        [Test]
        public void Resolve_HalfPrecisionOnInt_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => CodecRegistry.Resolve(typeof(int), QuantizationMode.HalfPrecision));
        }

        [Test]
        public void Resolve_HalfPrecisionOnQuaternion_Throws()
        {
            // Quaternion has 4 floats but is semantically not a Vec4 — half-precision is
            // wrong for it (smallest-three is the right tool). Surface the mismatch.
            Assert.Throws<InvalidOperationException>(
                () => CodecRegistry.Resolve(typeof(Quaternion), QuantizationMode.HalfPrecision));
        }

        [Test]
        public void Resolve_SmallestThreeOnVector3_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => CodecRegistry.Resolve(typeof(Vector3), QuantizationMode.SmallestThree));
        }

        // ---- IsValid mirror of Resolve ------------------------------------------

        [Test]
        public void IsValid_NoneOnAnyType_True()
        {
            Assert.IsTrue(CodecRegistry.IsValid(typeof(int), QuantizationMode.None));
            Assert.IsTrue(CodecRegistry.IsValid(typeof(Vector3), QuantizationMode.None));
            Assert.IsTrue(CodecRegistry.IsValid(typeof(Quaternion), QuantizationMode.None));
        }

        [Test]
        public void IsValid_HalfPrecisionOnFloat_True()
        {
            Assert.IsTrue(CodecRegistry.IsValid(typeof(float), QuantizationMode.HalfPrecision));
        }

        [Test]
        public void IsValid_HalfPrecisionOnInt_False()
        {
            Assert.IsFalse(CodecRegistry.IsValid(typeof(int), QuantizationMode.HalfPrecision));
        }

        [Test]
        public void IsValid_SmallestThreeOnVector3_False()
        {
            Assert.IsFalse(CodecRegistry.IsValid(typeof(Vector3), QuantizationMode.SmallestThree));
        }
    }
}
