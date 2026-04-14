using NUnit.Framework;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    /// <summary>
    /// Codec-level write→read symmetry. Each codec is verified against:
    ///   1. <see cref="IFieldCodec{T}.Size"/> matches the actual bytes written;
    ///   2. The decoded value equals the input within the codec's documented tolerance.
    /// </summary>
    [TestFixture]
    public class CodecRoundTripTests
    {
        private static T RoundTrip<T>(IFieldCodec<T> codec, T value, out int bytesWritten)
            where T : unmanaged
        {
            var writer = new FastBufferWriter(64, Allocator.Temp);
            try
            {
                int before = writer.Position;
                codec.Write(writer, value);
                bytesWritten = writer.Position - before;

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    return codec.Read(reader);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        // ---- FloatHalfCodec -----------------------------------------------------

        [Test]
        public void FloatHalfCodec_Size_IsTwoBytes()
        {
            var codec = new FloatHalfCodec();
            Assert.AreEqual(2, codec.Size);
        }

        [Test]
        public void FloatHalfCodec_Zero_RoundTripsExactly()
        {
            var codec = new FloatHalfCodec();
            var result = RoundTrip(codec, 0f, out int bytes);
            Assert.AreEqual(2, bytes);
            Assert.AreEqual(0f, result);
        }

        [Test]
        public void FloatHalfCodec_TypicalValue_RoundTripsWithinHalfTolerance()
        {
            // 1.0 has exact representation in half-float, so tolerance 0.001 is generous.
            var codec = new FloatHalfCodec();
            var result = RoundTrip(codec, 1.0f, out _);
            Assert.AreEqual(1.0f, result, 0.001f);
        }

        [Test]
        public void FloatHalfCodec_NegativeValue_PreservesSign()
        {
            var codec = new FloatHalfCodec();
            var result = RoundTrip(codec, -42.5f, out _);
            // Half precision near 42 is roughly 0.05; widen tolerance accordingly.
            Assert.AreEqual(-42.5f, result, 0.1f);
        }

        // ---- Vector3HalfCodec ---------------------------------------------------

        [Test]
        public void Vector3HalfCodec_Size_IsSixBytes()
        {
            var codec = new Vector3HalfCodec();
            Assert.AreEqual(6, codec.Size);
        }

        [Test]
        public void Vector3HalfCodec_TypicalPosition_RoundTripsWithinTolerance()
        {
            var codec = new Vector3HalfCodec();
            var input = new Vector3(12.5f, -7.25f, 0.125f);
            var result = RoundTrip(codec, input, out int bytes);
            Assert.AreEqual(6, bytes);
            // Half precision at magnitude ~10 is ~0.01; use 0.05 for safe margin.
            Assert.AreEqual(input.x, result.x, 0.05f);
            Assert.AreEqual(input.y, result.y, 0.05f);
            Assert.AreEqual(input.z, result.z, 0.05f);
        }

        [Test]
        public void Vector3HalfCodec_Zero_RoundTripsExactly()
        {
            var codec = new Vector3HalfCodec();
            var result = RoundTrip(codec, Vector3.zero, out _);
            Assert.AreEqual(Vector3.zero, result);
        }

        // ---- Vector2HalfCodec ---------------------------------------------------

        [Test]
        public void Vector2HalfCodec_Size_IsFourBytes()
        {
            var codec = new Vector2HalfCodec();
            Assert.AreEqual(4, codec.Size);
        }

        [Test]
        public void Vector2HalfCodec_TypicalValue_RoundTripsWithinTolerance()
        {
            var codec = new Vector2HalfCodec();
            var input = new Vector2(3.5f, -1.25f);
            var result = RoundTrip(codec, input, out int bytes);
            Assert.AreEqual(4, bytes);
            Assert.AreEqual(input.x, result.x, 0.01f);
            Assert.AreEqual(input.y, result.y, 0.01f);
        }

        // ---- Vector4HalfCodec ---------------------------------------------------

        [Test]
        public void Vector4HalfCodec_Size_IsEightBytes()
        {
            var codec = new Vector4HalfCodec();
            Assert.AreEqual(8, codec.Size);
        }

        [Test]
        public void Vector4HalfCodec_TypicalValue_RoundTripsWithinTolerance()
        {
            var codec = new Vector4HalfCodec();
            var input = new Vector4(1f, 2f, 3f, 4f);
            var result = RoundTrip(codec, input, out int bytes);
            Assert.AreEqual(8, bytes);
            Assert.AreEqual(input.x, result.x, 0.01f);
            Assert.AreEqual(input.y, result.y, 0.01f);
            Assert.AreEqual(input.z, result.z, 0.01f);
            Assert.AreEqual(input.w, result.w, 0.01f);
        }

        // ---- QuaternionSmallestThreeCodec ---------------------------------------

        [Test]
        public void QuaternionSmallestThreeCodec_Size_IsFourBytes()
        {
            var codec = new QuaternionSmallestThreeCodec();
            Assert.AreEqual(4, codec.Size);
        }

        [Test]
        public void QuaternionSmallestThreeCodec_Identity_RoundTripsToIdentity()
        {
            // q == identity, w is the largest component (=1) so it gets dropped and
            // reconstructed from sqrt(1 - 0 - 0 - 0) = 1.
            var codec = new QuaternionSmallestThreeCodec();
            var result = RoundTrip(codec, Quaternion.identity, out int bytes);
            Assert.AreEqual(4, bytes);
            Assert.AreEqual(1f, Mathf.Abs(Quaternion.Dot(Quaternion.identity, result)), 0.001f);
        }

        [Test]
        public void QuaternionSmallestThreeCodec_TypicalRotation_RoundTripsWithinTolerance()
        {
            // Dot product on unit quaternions equals cos(half-angle). |dot| > 0.9999 means
            // angular error under ~1°, which is the documented bound for smallest-three.
            // We use abs because q and -q encode the same rotation and the codec may sign-flip.
            var codec = new QuaternionSmallestThreeCodec();
            var input = Quaternion.Euler(30f, 60f, 90f);
            var result = RoundTrip(codec, input, out _);
            float dot = Mathf.Abs(Quaternion.Dot(input, result));
            Assert.GreaterOrEqual(dot, 0.999f, $"angular error too large: dot={dot}");
        }

        [Test]
        public void QuaternionSmallestThreeCodec_NegativeWComponent_RoundTripsCorrectly()
        {
            // q with w<0 — the codec must sign-flip the whole quaternion so the dropped
            // (largest, here likely w) component is positive; the reconstructed rotation
            // should still match the input modulo the q=−q equivalence.
            var codec = new QuaternionSmallestThreeCodec();
            var input = new Quaternion(0.1f, 0.2f, 0.3f, -0.927f).normalized;
            var result = RoundTrip(codec, input, out _);
            float dot = Mathf.Abs(Quaternion.Dot(input, result));
            Assert.GreaterOrEqual(dot, 0.999f);
        }

        [Test]
        public void QuaternionSmallestThreeCodec_LargestComponentNotW_RoundTripsCorrectly()
        {
            // 90° rotation around X: x≈0.707, w≈0.707 — guard against the codec assuming
            // w is always largest. Picks index 0 or 3 depending on tie-break order; either
            // must reconstruct to the same rotation.
            var codec = new QuaternionSmallestThreeCodec();
            var input = Quaternion.AngleAxis(90f, Vector3.right);
            var result = RoundTrip(codec, input, out _);
            float dot = Mathf.Abs(Quaternion.Dot(input, result));
            Assert.GreaterOrEqual(dot, 0.999f);
        }

        // ---- RawCodec -----------------------------------------------------------

        [Test]
        public void RawCodec_Size_MatchesSizeofT()
        {
            unsafe
            {
                Assert.AreEqual(sizeof(int), new RawCodec<int>().Size);
                Assert.AreEqual(sizeof(Vector3), new RawCodec<Vector3>().Size);
                Assert.AreEqual(sizeof(Quaternion), new RawCodec<Quaternion>().Size);
            }
        }

        [Test]
        public void RawCodec_Vector3_RoundTripsBitExact()
        {
            // Tolerance 0f — RawCodec is memcpy and must not lose any bit.
            var codec = new RawCodec<Vector3>();
            var input = new Vector3(Mathf.PI, -Mathf.PI / 2f, Mathf.Epsilon);
            var result = RoundTrip(codec, input, out _);
            Assert.AreEqual(input.x, result.x, 0f);
            Assert.AreEqual(input.y, result.y, 0f);
            Assert.AreEqual(input.z, result.z, 0f);
        }
    }
}
