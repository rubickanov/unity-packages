using System.Collections.Generic;
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

        private readonly List<ReplicatedFieldBinding> _bindings = new();

        private (InterpolatedFieldBinding<T> binding, ReactiveProperty<T> reactive)
            CreateBinding<T>(T initial = default) where T : unmanaged
        {
            var reactive = new ReactiveProperty<T>(initial);
            var binding = (InterpolatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), FieldBindingKind.PassiveInterpolated);
            _bindings.Add(binding);
            return (binding, reactive);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var binding in _bindings)
                binding.OnDespawn();
            _bindings.Clear();
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
        public void TickRender_EmptyBuffer_DoesNotMutateInterpolatedValue()
        {
            var (binding, _) = CreateBinding<Vector3>();

            binding.TickRender(0.0);
            binding.TickRender(1.0);
            binding.TickRender(100.0);

            Assert.AreEqual(default(Vector3), binding.InterpolatedValue);
        }

        [Test]
        public void Bootstrap_FirstSnapshot_AppliesImmediatelyWithoutTickRender()
        {
            var (binding, reactive) = CreateBinding<Vector3>();
            var value = new Vector3(5f, 10f, 15f);

            PushSnapshot(binding, value, time: 1.0);

            Assert.AreEqual(value, reactive.Value);
            Assert.AreEqual(value, binding.InterpolatedValue);
        }

        [Test]
        public void TickRender_SingleSnapshot_HoldsThatValueForAnyRenderTime()
        {
            var (binding, _) = CreateBinding<Vector3>();
            var snap = new Vector3(3f, 3f, 3f);
            PushSnapshot(binding, snap, time: 1.0);

            binding.TickRender(-50.0);
            Assert.AreEqual(snap, binding.InterpolatedValue);
            binding.TickRender(1.0);
            Assert.AreEqual(snap, binding.InterpolatedValue);
            binding.TickRender(50.0);
            Assert.AreEqual(snap, binding.InterpolatedValue);
        }

        // ---- Interpolation between snapshots -----------------------------------

        [Test]
        public void TickRender_BetweenSnapshots_LerpsFloatWithCorrectAlpha()
        {
            var (binding, _) = CreateBinding<float>();
            PushSnapshot(binding, 0f, time: 1.0);
            PushSnapshot(binding, 10f, time: 2.0);

            // renderTime = 1.25 → alpha = 0.25 → 0 + 0.25 * 10 = 2.5
            binding.TickRender(1.25);

            Assert.AreEqual(2.5f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void TickRender_BetweenSnapshots_LerpsVector3AtMidpoint()
        {
            var (binding, _) = CreateBinding<Vector3>();
            PushSnapshot(binding, Vector3.zero, time: 0.0);
            PushSnapshot(binding, new Vector3(10f, 10f, 10f), time: 1.0);

            binding.TickRender(0.5);

            Assert.AreEqual(5f, binding.InterpolatedValue.x, 1e-5f);
            Assert.AreEqual(5f, binding.InterpolatedValue.y, 1e-5f);
            Assert.AreEqual(5f, binding.InterpolatedValue.z, 1e-5f);
        }

        [Test]
        public void TickRender_QuaternionMidpoint_PreservesUnitLength()
        {
            var (binding, _) = CreateBinding(Quaternion.identity);
            PushSnapshot(binding, Quaternion.identity, time: 0.0);
            PushSnapshot(binding, Quaternion.Euler(0f, 120f, 0f), time: 1.0);

            binding.TickRender(0.5);

            float length = Mathf.Sqrt(
                binding.InterpolatedValue.x * binding.InterpolatedValue.x +
                binding.InterpolatedValue.y * binding.InterpolatedValue.y +
                binding.InterpolatedValue.z * binding.InterpolatedValue.z +
                binding.InterpolatedValue.w * binding.InterpolatedValue.w);
            Assert.AreEqual(1f, length, 1e-4f);
        }

        // ---- Out-of-bounds render times ----------------------------------------

        [Test]
        public void TickRender_BeforeOldestSnapshot_HoldsOldest()
        {
            var (binding, _) = CreateBinding<float>();
            PushSnapshot(binding, 100f, time: 5.0);
            PushSnapshot(binding, 200f, time: 6.0);

            binding.TickRender(0.0);

            Assert.AreEqual(100f, binding.InterpolatedValue, 0f);
        }

        [Test]
        public void TickRender_AfterNewestSnapshot_HoldsNewest_NoExtrapolation()
        {
            var (binding, _) = CreateBinding<float>();
            PushSnapshot(binding, 100f, time: 5.0);
            PushSnapshot(binding, 200f, time: 6.0);

            binding.TickRender(9999.0);

            Assert.AreEqual(200f, binding.InterpolatedValue, 0f);
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
            var (binding, _) = CreateBinding<float>();
            for (int i = 0; i < 40; i++)
                PushSnapshot(binding, i, time: i);

            binding.TickRender(8.0);

            Assert.AreEqual(8f, binding.InterpolatedValue, 0f);
        }

        [Test]
        public void RingBuffer_AfterWraparound_NewestSampleIsTheMostRecentPush()
        {
            var (binding, _) = CreateBinding<float>();
            for (int i = 0; i < 40; i++)
                PushSnapshot(binding, i, time: i);

            binding.TickRender(39.0);

            Assert.AreEqual(39f, binding.InterpolatedValue, 0f);
        }

        [Test]
        public void RingBuffer_AfterWraparound_LerpBetweenAnyPairUsesCorrectValues()
        {
            var (binding, _) = CreateBinding<float>();
            for (int i = 0; i < 40; i++)
                PushSnapshot(binding, i, time: i);

            binding.TickRender(20.5);

            Assert.AreEqual(20.5f, binding.InterpolatedValue, 1e-5f);
        }

        // ---- .Value vs .Smooth() separation ------------------------------------

        [Test]
        public void TickRender_DoesNotMutateReactiveValue_ValueRemainsRaw()
        {
            var (binding, reactive) = CreateBinding<float>();
            PushSnapshot(binding, 0f, time: 1.0);
            PushSnapshot(binding, 10f, time: 2.0);

            binding.TickRender(1.5);

            Assert.AreEqual(5f, binding.InterpolatedValue, 1e-5f);
            // .Value must hold the latest raw snapshot (10f), not the interpolated result.
            Assert.AreEqual(10f, reactive.Value, 1e-5f);
        }

        [Test]
        public void ApplyFromNetwork_AlwaysWritesRawToReactive()
        {
            var (binding, reactive) = CreateBinding<float>();

            PushSnapshot(binding, 1f, time: 1.0);
            Assert.AreEqual(1f, reactive.Value, 1e-5f);

            PushSnapshot(binding, 5f, time: 2.0);
            Assert.AreEqual(5f, reactive.Value, 1e-5f);

            PushSnapshot(binding, 99f, time: 3.0);
            Assert.AreEqual(99f, reactive.Value, 1e-5f);
        }

        [Test]
        public void Smooth_ReturnsInterpolatedValue_WhenRegistered()
        {
            var (binding, reactive) = CreateBinding<float>();
            PushSnapshot(binding, 0f, time: 1.0);
            PushSnapshot(binding, 10f, time: 2.0);

            binding.TickRender(1.5);

            Assert.AreEqual(5f, reactive.Smooth(), 1e-5f);
        }

        [Test]
        public void Smooth_FallsBackToValue_WhenNotRegistered()
        {
            var reactive = new ReactiveProperty<float>(42f);

            Assert.AreEqual(42f, reactive.Smooth(), 1e-5f);
        }

        [Test]
        public void Smooth_FallsBackToValue_AfterDespawn()
        {
            var (binding, reactive) = CreateBinding<float>();
            PushSnapshot(binding, 0f, time: 1.0);
            PushSnapshot(binding, 10f, time: 2.0);
            binding.TickRender(1.5);

            binding.OnDespawn();

            // After despawn, Smooth falls back to .Value (latest raw = 10f).
            Assert.AreEqual(10f, reactive.Smooth(), 1e-5f);
        }

        [Test]
        public void ClearInterpolationState_ResetsBufferAndInterpolatedValue()
        {
            var (binding, reactive) = CreateBinding<float>();
            PushSnapshot(binding, 5f, time: 1.0);
            PushSnapshot(binding, 15f, time: 2.0);
            binding.TickRender(1.5);
            Assert.AreEqual(10f, binding.InterpolatedValue, 1e-5f);

            binding.ClearInterpolationState();

            Assert.AreEqual(0, GetCount(binding));
            Assert.AreEqual(default(float), binding.InterpolatedValue);
            // .Value retains the last raw write — ClearInterpolationState only resets the buffer.
            Assert.AreEqual(15f, reactive.Value, 1e-5f);
        }
    }
}
