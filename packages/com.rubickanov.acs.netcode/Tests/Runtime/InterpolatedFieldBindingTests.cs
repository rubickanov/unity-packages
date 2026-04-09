using System.Reflection;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class InterpolatedFieldBindingTests
    {
        // ---- Helpers ------------------------------------------------------------

        private static (InterpolatedFieldBinding<T> binding, ReactiveProperty<T> reactive)
            CreateBinding<T>(T initial = default) where T : unmanaged
        {
            var reactive = new ReactiveProperty<T>(initial);
            var binding = (InterpolatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), interpolate: true);
            return (binding, reactive);
        }

        /// <summary>
        /// Mimics the network path: write <paramref name="value"/> into a FastBufferWriter,
        /// read it back into <paramref name="binding"/>, then call ApplyFromNetwork with the
        /// given snapshot time.
        /// </summary>
        private static unsafe void PushSnapshot<T>(InterpolatedFieldBinding<T> binding, T value, double time)
            where T : unmanaged
        {
            var writer = new FastBufferWriter(sizeof(T), Allocator.Temp);
            try
            {
                writer.WriteBytesSafe((byte*)&value, sizeof(T));
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    binding.ReadFrom(reader);
                    binding.ApplyFromNetwork(time);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        private static int GetCount<T>(InterpolatedFieldBinding<T> binding) where T : unmanaged
        {
            var field = typeof(InterpolatedFieldBinding<T>)
                .GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)field.GetValue(binding);
        }

        // ---- Empty / single snapshot -------------------------------------------

        [Test]
        public void TickRender_EmptyBuffer_DoesNotMutateReactiveValue()
        {
            // Sentinel value proves TickRender short-circuits when _count == 0:
            // if the method accidentally touches the buffer, sentinel would be overwritten
            // with default(Vector3) from uninitialised slot [0].
            var sentinel = new Vector3(777f, 777f, 777f);
            var (binding, reactive) = CreateBinding(sentinel);

            binding.TickRender(0.0);
            binding.TickRender(1.0);
            binding.TickRender(100.0);

            Assert.AreEqual(sentinel, reactive.Value);
        }

        [Test]
        public void Bootstrap_FirstSnapshot_AppliesImmediatelyWithoutTickRender()
        {
            // Without bootstrap, the entity would hold default(T) for ≈2 ticks of
            // interpolation delay. The InterpolatedFieldBinding explicitly WriteSuppressed's
            // the first pending value — this test is the canary for that branch.
            var (binding, reactive) = CreateBinding<Vector3>();
            var value = new Vector3(5f, 10f, 15f);

            PushSnapshot(binding, value, time: 1.0);

            Assert.AreEqual(value, reactive.Value);
        }

        [Test]
        public void TickRender_SingleSnapshot_HoldsThatValueForAnyRenderTime()
        {
            var (binding, reactive) = CreateBinding<Vector3>();
            var snap = new Vector3(3f, 3f, 3f);
            PushSnapshot(binding, snap, time: 1.0);

            // Any render time — past, present, future — must return the only sample.
            binding.TickRender(-50.0);
            Assert.AreEqual(snap, reactive.Value);
            binding.TickRender(1.0);
            Assert.AreEqual(snap, reactive.Value);
            binding.TickRender(50.0);
            Assert.AreEqual(snap, reactive.Value);
        }

        // ---- Interpolation between snapshots -----------------------------------

        [Test]
        public void TickRender_BetweenSnapshots_LerpsFloatWithCorrectAlpha()
        {
            var (binding, reactive) = CreateBinding<float>();
            PushSnapshot(binding, 0f, time: 1.0);
            PushSnapshot(binding, 10f, time: 2.0);

            // renderTime = 1.25 → alpha = 0.25 → 0 + 0.25 * 10 = 2.5
            binding.TickRender(1.25);

            Assert.AreEqual(2.5f, reactive.Value, 1e-5f);
        }

        [Test]
        public void TickRender_BetweenSnapshots_LerpsVector3AtMidpoint()
        {
            var (binding, reactive) = CreateBinding<Vector3>();
            PushSnapshot(binding, Vector3.zero, time: 0.0);
            PushSnapshot(binding, new Vector3(10f, 10f, 10f), time: 1.0);

            binding.TickRender(0.5);

            Assert.AreEqual(5f, reactive.Value.x, 1e-5f);
            Assert.AreEqual(5f, reactive.Value.y, 1e-5f);
            Assert.AreEqual(5f, reactive.Value.z, 1e-5f);
        }

        [Test]
        public void TickRender_QuaternionMidpoint_PreservesUnitLength()
        {
            // The invariant that distinguishes Slerp from naive Lerp: mid-rotation must
            // stay on the unit sphere. Naive Lerp between two unit quaternions shrinks
            // at the midpoint — this test fails if the lerper is swapped to Quaternion.Lerp.
            var (binding, reactive) = CreateBinding(Quaternion.identity);
            PushSnapshot(binding, Quaternion.identity, time: 0.0);
            PushSnapshot(binding, Quaternion.Euler(0f, 120f, 0f), time: 1.0);

            binding.TickRender(0.5);

            float length = Mathf.Sqrt(
                reactive.Value.x * reactive.Value.x +
                reactive.Value.y * reactive.Value.y +
                reactive.Value.z * reactive.Value.z +
                reactive.Value.w * reactive.Value.w);
            Assert.AreEqual(1f, length, 1e-4f);
        }

        // ---- Out-of-bounds render times ----------------------------------------

        [Test]
        public void TickRender_BeforeOldestSnapshot_HoldsOldest()
        {
            var (binding, reactive) = CreateBinding<float>();
            PushSnapshot(binding, 100f, time: 5.0);
            PushSnapshot(binding, 200f, time: 6.0);

            binding.TickRender(0.0);

            Assert.AreEqual(100f, reactive.Value, 0f);
        }

        [Test]
        public void TickRender_AfterNewestSnapshot_HoldsNewest_NoExtrapolation()
        {
            var (binding, reactive) = CreateBinding<float>();
            PushSnapshot(binding, 100f, time: 5.0);
            PushSnapshot(binding, 200f, time: 6.0);

            binding.TickRender(9999.0);

            // Holds newest — no extrapolation beyond the buffer.
            Assert.AreEqual(200f, reactive.Value, 0f);
        }

        // ---- Ring buffer wraparound --------------------------------------------

        [Test]
        public void RingBuffer_FortyPushesIntoThirtyTwoCapacity_CountCapsAtThirtyTwo()
        {
            var (binding, _) = CreateBinding<float>();

            for (int i = 0; i < 40; i++)
                PushSnapshot(binding, i, time: i);

            Assert.AreEqual(32, GetCount(binding));
        }

        [Test]
        public void RingBuffer_AfterWraparound_OldestSampleIsTheOneAtIndexEight()
        {
            // After 40 pushes into a 32-capacity ring, samples 0..7 must have been evicted
            // and the oldest retained sample is i=8. TickRender at t=8 (on the new oldest)
            // must therefore return 8f.
            var (binding, reactive) = CreateBinding<float>();
            for (int i = 0; i < 40; i++)
                PushSnapshot(binding, i, time: i);

            binding.TickRender(8.0);

            Assert.AreEqual(8f, reactive.Value, 0f);
        }

        [Test]
        public void RingBuffer_AfterWraparound_NewestSampleIsTheMostRecentPush()
        {
            // The newest must always be the last pushed regardless of wraparound position.
            var (binding, reactive) = CreateBinding<float>();
            for (int i = 0; i < 40; i++)
                PushSnapshot(binding, i, time: i);

            binding.TickRender(39.0);

            Assert.AreEqual(39f, reactive.Value, 0f);
        }

        [Test]
        public void RingBuffer_AfterWraparound_LerpBetweenAnyPairUsesCorrectValues()
        {
            // Pick a pair in the middle of the surviving window (well after wraparound).
            // If the oldest-index math is wrong post-wraparound, lerp picks the wrong pair.
            var (binding, reactive) = CreateBinding<float>();
            for (int i = 0; i < 40; i++)
                PushSnapshot(binding, i, time: i);

            binding.TickRender(20.5);

            Assert.AreEqual(20.5f, reactive.Value, 1e-5f);
        }
    }
}
