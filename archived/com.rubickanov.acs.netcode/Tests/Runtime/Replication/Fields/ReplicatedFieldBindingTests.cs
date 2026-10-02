using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class ReplicatedFieldBindingTests
    {
        // ---- Round-trip helpers -------------------------------------------------

        /// <summary>
        /// Sender binding writes <paramref name="value"/> through FastBufferWriter, a fresh
        /// receiver binding reads the bytes back and applies them. Returns the applied value
        /// on the receiver. If round-trip is byte-exact, the returned value equals the input.
        /// </summary>
        private static unsafe T RoundTrip<T>(T value) where T : unmanaged
        {
            var src = new ReactiveProperty<T>(value);
            var dst = new ReactiveProperty<T>(default);

            var sender = (ReplicatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(src, typeof(T), FieldBindingKind.Plain);
            var receiver = (ReplicatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(dst, typeof(T), FieldBindingKind.Plain);

            var writer = new FastBufferWriter(sizeof(T), Allocator.Temp);
            try
            {
                sender.WriteTo(writer);

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    receiver.ReadFrom(reader);
                    receiver.ApplyFromNetwork(0);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }

            return dst.Value;
        }

        private static ReplicatedFieldBinding<T> CreateBinding<T>(ReactiveProperty<T> reactive)
            where T : unmanaged
        {
            return (ReplicatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), FieldBindingKind.Plain);
        }

        // ---- Round-trip per unmanaged type --------------------------------------

        [Test]
        public void WriteRead_Int_PreservesBitExact()
        {
            // 0x12345678 is a non-trivial int whose bytes differ in every position —
            // exposes endianness or byte-copy range bugs that symmetric values (e.g. 0, 1) hide.
            Assert.AreEqual(0x12345678, RoundTrip(0x12345678));
        }

        [Test]
        public void WriteRead_NegativeInt_PreservesSignBit()
        {
            Assert.AreEqual(-42, RoundTrip(-42));
        }

        [Test]
        public void WriteRead_Float_PreservesBitExact()
        {
            // Tolerance 0f — round-trip is a memcpy, not a lossy conversion.
            Assert.AreEqual(Mathf.PI, RoundTrip(Mathf.PI), 0f);
        }

        [Test]
        public void WriteRead_BoolTrue_PreservesValue()
        {
            Assert.IsTrue(RoundTrip(true));
        }

        [Test]
        public void WriteRead_BoolFalse_PreservesValue()
        {
            Assert.IsFalse(RoundTrip(false));
        }

        [Test]
        public void WriteRead_Vector2_PreservesBothComponents()
        {
            var input = new Vector2(1.23f, -4.56f);
            var result = RoundTrip(input);
            Assert.AreEqual(input.x, result.x, 0f);
            Assert.AreEqual(input.y, result.y, 0f);
        }

        [Test]
        public void WriteRead_Vector3_PreservesAllComponents()
        {
            var input = new Vector3(1.23f, -4.56f, 7.89f);
            var result = RoundTrip(input);
            Assert.AreEqual(input.x, result.x, 0f);
            Assert.AreEqual(input.y, result.y, 0f);
            Assert.AreEqual(input.z, result.z, 0f);
        }

        [Test]
        public void WriteRead_Vector4_PreservesAllComponents()
        {
            var input = new Vector4(1f, 2f, 3f, 4f);
            Assert.AreEqual(input, RoundTrip(input));
        }

        [Test]
        public void WriteRead_Quaternion_PreservesAllComponents()
        {
            var input = Quaternion.Euler(30f, 60f, 90f);
            var result = RoundTrip(input);
            // Per-component exact — Quaternion round-trip is 16 bytes memcpy.
            Assert.AreEqual(input.x, result.x, 0f);
            Assert.AreEqual(input.y, result.y, 0f);
            Assert.AreEqual(input.z, result.z, 0f);
            Assert.AreEqual(input.w, result.w, 0f);
        }

        [Test]
        public void WriteRead_Color_PreservesAllChannels()
        {
            var input = new Color(0.1f, 0.25f, 0.5f, 0.75f);
            var result = RoundTrip(input);
            Assert.AreEqual(input.r, result.r, 0f);
            Assert.AreEqual(input.g, result.g, 0f);
            Assert.AreEqual(input.b, result.b, 0f);
            Assert.AreEqual(input.a, result.a, 0f);
        }

        [Test]
        public void WriteRead_CustomUnmanagedStruct_PreservesAllFields()
        {
            var input = new PackedStruct { A = 0x11223344, B = -7.25f, C = 0xAB };
            var result = RoundTrip(input);
            Assert.AreEqual(input.A, result.A);
            Assert.AreEqual(input.B, result.B, 0f);
            Assert.AreEqual(input.C, result.C);
        }

        // ---- Skip ---------------------------------------------------------------

        [Test]
        public unsafe void Skip_Int_AdvancesReaderPositionByFourBytes()
        {
            var reactive = new ReactiveProperty<int>(0x55AA55AA);
            var binding = CreateBinding(reactive);

            var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            try
            {
                binding.WriteTo(writer);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    int before = reader.Position;
                    binding.Skip(reader);
                    int after = reader.Position;
                    Assert.AreEqual(sizeof(int), after - before);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        [Test]
        public unsafe void Skip_Vector3_AdvancesReaderPositionBySizeOfVector3()
        {
            var reactive = new ReactiveProperty<Vector3>(new Vector3(1, 2, 3));
            var binding = CreateBinding(reactive);

            var writer = new FastBufferWriter(sizeof(Vector3), Allocator.Temp);
            try
            {
                binding.WriteTo(writer);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    int before = reader.Position;
                    binding.Skip(reader);
                    int after = reader.Position;
                    Assert.AreEqual(sizeof(Vector3), after - before);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        [Test]
        public unsafe void Skip_DoesNotMutateReactiveValue()
        {
            // Regression guard: Skip must not write anything back to the reactive property.
            // A buggy implementation that calls ReadFrom+ApplyFromNetwork would overwrite the value.
            var sender = new ReactiveProperty<int>(1);
            var receiver = new ReactiveProperty<int>(999);
            var senderBinding = CreateBinding(sender);
            var receiverBinding = CreateBinding(receiver);

            var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            try
            {
                senderBinding.WriteTo(writer);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    receiverBinding.Skip(reader);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }

            Assert.AreEqual(999, receiver.Value);
        }

        // ---- Dirty lifecycle ----------------------------------------------------

        [Test]
        public void IsDirty_InitialBindingWithoutSubscription_IsFalse()
        {
            var reactive = new ReactiveProperty<int>(0);
            var binding = CreateBinding(reactive);
            Assert.IsFalse(binding.IsDirty);
        }

        [Test]
        public void IsDirty_AfterAuthoritySubscribeAndValueChange_BecomesTrue()
        {
            var reactive = new ReactiveProperty<int>(0);
            var binding = CreateBinding(reactive);
            var bag = new DisposableBag();
            try
            {
                binding.SubscribeAsAuthority(ref bag);
                // R3 ReactiveProperty replays current value on Subscribe, so IsDirty
                // goes true immediately. Clear it so the next change is what we measure.
                binding.ClearDirty();

                reactive.Value = 42;

                Assert.IsTrue(binding.IsDirty);
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void ClearDirty_AfterMarkedDirty_ResetsFlag()
        {
            var reactive = new ReactiveProperty<int>(0);
            var binding = CreateBinding(reactive);
            binding.MarkDirty();
            Assert.IsTrue(binding.IsDirty, "precondition: MarkDirty must set the flag");

            binding.ClearDirty();

            Assert.IsFalse(binding.IsDirty);
        }

        [Test]
        public void ApplyFromNetwork_OnSubscribedReceiver_DoesNotReMarkDirty()
        {
            // Feedback-loop regression: when a non-authority peer applies an incoming
            // network value via WriteSuppressed, the authority-subscription callback must
            // NOT see the change as "local write" and re-raise IsDirty.
            var src = new ReactiveProperty<int>(0);
            var dst = new ReactiveProperty<int>(0);
            var sender = CreateBinding(src);
            var receiver = CreateBinding(dst);

            var bag = new DisposableBag();
            try
            {
                receiver.SubscribeAsAuthority(ref bag);
                receiver.ClearDirty();

                src.Value = 77;
                unsafe
                {
                    var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
                    try
                    {
                        sender.WriteTo(writer);
                        var reader = new FastBufferReader(writer, Allocator.Temp);
                        try
                        {
                            receiver.ReadFrom(reader);
                            receiver.ApplyFromNetwork(0);
                        }
                        finally { reader.Dispose(); }
                    }
                    finally { writer.Dispose(); }
                }

                Assert.AreEqual(77, dst.Value, "precondition: receiver must have absorbed the value");
                Assert.IsFalse(receiver.IsDirty,
                    "WriteSuppressed must suppress the subscription callback so the peer " +
                    "does not echo the network-received value back as a dirty local write.");
            }
            finally { bag.Dispose(); }
        }

        // ---- OwnerWroteSinceSpawn -----------------------------------------------

        [Test]
        public void OwnerWroteSinceSpawn_NewBinding_IsFalse()
        {
            // A freshly-built binding has not been subscribed and nothing has written
            // through it — the flag must be false. If this ever flips, the owner-auth
            // initial-sync fast path blocks every legitimate snapshot.
            var reactive = new ReactiveProperty<int>(0);
            var binding = CreateBinding(reactive);
            Assert.IsFalse(binding.OwnerWroteSinceSpawn);
        }

        [Test]
        public void OwnerWroteSinceSpawn_AfterAuthoritySubscribeReplay_IsTrue_RequiresExplicitReset()
        {
            // Regression contract: R3 ReactiveProperty replays the current value on
            // Subscribe, so SubscribeAsAuthority synthesizes a "write" immediately.
            // This test pins that behaviour so EntityReplicator.OnNetworkSpawn MUST
            // keep calling ResetOwnerWroteSinceSpawn right after the subscribe — if
            // this test starts failing, either R3 changed semantics or someone removed
            // the flag assignment from the subscribe callback, and both branches need
            // to re-examine the reset call in OnNetworkSpawn.
            var reactive = new ReactiveProperty<int>(0);
            var binding = CreateBinding(reactive);
            var bag = new DisposableBag();
            try
            {
                binding.SubscribeAsAuthority(ref bag);

                Assert.IsTrue(binding.OwnerWroteSinceSpawn,
                    "subscribe replay MUST set the flag — OnNetworkSpawn relies on this to " +
                    "know that an explicit ResetOwnerWroteSinceSpawn is required after subscribe.");
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void OwnerWroteSinceSpawn_AfterResetAndAuthorityWrite_IsTrue()
        {
            var reactive = new ReactiveProperty<int>(0);
            var binding = CreateBinding(reactive);
            var bag = new DisposableBag();
            try
            {
                binding.SubscribeAsAuthority(ref bag);
                binding.ResetOwnerWroteSinceSpawn();
                Assert.IsFalse(binding.OwnerWroteSinceSpawn, "precondition: reset must zero the flag");

                reactive.Value = 123;

                Assert.IsTrue(binding.OwnerWroteSinceSpawn,
                    "a real authority-side write after reset must flip the flag back to true.");
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void OwnerWroteSinceSpawn_AfterResetAndSuppressedWrite_RemainsFalse()
        {
            // Core suppression contract: when a non-authority peer receives state via
            // ApplyFromNetwork → WriteSuppressed, the subscribe callback sees the change
            // but bails out on _suppressNotification. OwnerWroteSinceSpawn must stay
            // false — otherwise initial-sync on the pure-client owner would permanently
            // skip every owner-auth field after the first snapshot, re-introducing the
            // permanent-default failure mode that #19 exists to avoid.
            var src = new ReactiveProperty<int>(0);
            var dst = new ReactiveProperty<int>(0);
            var sender = CreateBinding(src);
            var receiver = CreateBinding(dst);

            var bag = new DisposableBag();
            try
            {
                receiver.SubscribeAsAuthority(ref bag);
                receiver.ResetOwnerWroteSinceSpawn();

                src.Value = 77;
                unsafe
                {
                    var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
                    try
                    {
                        sender.WriteTo(writer);
                        var reader = new FastBufferReader(writer, Allocator.Temp);
                        try
                        {
                            receiver.ReadFrom(reader);
                            receiver.ApplyFromNetwork(0);
                        }
                        finally { reader.Dispose(); }
                    }
                    finally { writer.Dispose(); }
                }

                Assert.AreEqual(77, dst.Value, "precondition: receiver must have absorbed the value");
                Assert.IsFalse(receiver.OwnerWroteSinceSpawn,
                    "WriteSuppressed must NOT flip OwnerWroteSinceSpawn — the flag only tracks " +
                    "real local authority writes, not applied network snapshots.");
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void ResetOwnerWroteSinceSpawn_IsIdempotentAcrossSuccessiveWrites()
        {
            // Guard rail against a subtle refactor bug: a typo'd Reset that disposed
            // the subscription or cleared _suppressNotification could stop future
            // authority writes from ever flipping the flag. Round-trip the full cycle
            // twice to ensure Reset does not break subscribe.
            var reactive = new ReactiveProperty<int>(0);
            var binding = CreateBinding(reactive);
            var bag = new DisposableBag();
            try
            {
                binding.SubscribeAsAuthority(ref bag);
                binding.ResetOwnerWroteSinceSpawn();

                reactive.Value = 1;
                Assert.IsTrue(binding.OwnerWroteSinceSpawn);

                binding.ResetOwnerWroteSinceSpawn();
                Assert.IsFalse(binding.OwnerWroteSinceSpawn);

                reactive.Value = 2;
                Assert.IsTrue(binding.OwnerWroteSinceSpawn,
                    "Reset must not break the subscribe — subsequent writes still flip the flag.");
            }
            finally { bag.Dispose(); }
        }

        // ---- Quantization -------------------------------------------------------

        [Test]
        public void WriteTo_QuantizedVector3_WritesSixBytes()
        {
            // HalfPrecision Vector3 → 3× half-float = 6B. Factory must thread the
            // quantization mode through CodecRegistry into the binding's codec.
            var reactive = new ReactiveProperty<Vector3>(new Vector3(1f, 2f, 3f));
            var binding = (ReplicatedFieldBinding<Vector3>)ReplicatedFieldBindingFactory.Create(
                reactive, typeof(Vector3), FieldBindingKind.Plain, 0, QuantizationMode.HalfPrecision);

            unsafe
            {
                var writer = new FastBufferWriter(sizeof(Vector3), Allocator.Temp);
                try
                {
                    int before = writer.Position;
                    binding.WriteTo(writer);
                    Assert.AreEqual(6, writer.Position - before);
                }
                finally { writer.Dispose(); }
            }
        }

        [Test]
        public void WriteTo_QuantizedQuaternion_WritesFourBytes()
        {
            // SmallestThree Quaternion → 2-bit index + 3× 10-bit signed = 32 bits = 4B.
            var reactive = new ReactiveProperty<Quaternion>(Quaternion.identity);
            var binding = (ReplicatedFieldBinding<Quaternion>)ReplicatedFieldBindingFactory.Create(
                reactive, typeof(Quaternion), FieldBindingKind.Plain, 0, QuantizationMode.SmallestThree);

            unsafe
            {
                var writer = new FastBufferWriter(sizeof(Quaternion), Allocator.Temp);
                try
                {
                    int before = writer.Position;
                    binding.WriteTo(writer);
                    Assert.AreEqual(4, writer.Position - before);
                }
                finally { writer.Dispose(); }
            }
        }

        [Test]
        public void RoundTrip_QuantizedVector3_WithinHalfTolerance()
        {
            // End-to-end smoke: factory → binding → wire → binding → reactive. Tolerance
            // ≈0.05 covers half-float error at magnitude ~10 with margin. Guards against
            // a factory path that silently uses RawCodec even when quantization is set.
            var src = new ReactiveProperty<Vector3>(new Vector3(12.5f, -7.25f, 0.125f));
            var dst = new ReactiveProperty<Vector3>(Vector3.zero);
            var sender = (ReplicatedFieldBinding<Vector3>)ReplicatedFieldBindingFactory.Create(
                src, typeof(Vector3), FieldBindingKind.Plain, 0, QuantizationMode.HalfPrecision);
            var receiver = (ReplicatedFieldBinding<Vector3>)ReplicatedFieldBindingFactory.Create(
                dst, typeof(Vector3), FieldBindingKind.Plain, 0, QuantizationMode.HalfPrecision);

            var writer = new FastBufferWriter(16, Allocator.Temp);
            try
            {
                sender.WriteTo(writer);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    receiver.ReadFrom(reader);
                    receiver.ApplyFromNetwork(0);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }

            Assert.AreEqual(12.5f, dst.Value.x, 0.05f);
            Assert.AreEqual(-7.25f, dst.Value.y, 0.05f);
            Assert.AreEqual(0.125f, dst.Value.z, 0.05f);
        }

        [Test]
        public void RoundTrip_QuantizedVector3WithInterpolation_LerpsBetweenSnapshots()
        {
            // Quantization + Interpolation must compose: each incoming snapshot is decoded
            // (via FloatHalfCodec ×3) into the interpolation buffer, then TickRender lerps
            // in T-space exactly as for the raw case. This is the main real-world config
            // (Vector3 position replicated with half precision + linear smoothing).
            var src = new ReactiveProperty<Vector3>(new Vector3(0f, 0f, 0f));
            var dst = new ReactiveProperty<Vector3>(Vector3.zero);
            var sender = (ReplicatedFieldBinding<Vector3>)ReplicatedFieldBindingFactory.Create(
                src, typeof(Vector3), FieldBindingKind.Plain, 0, QuantizationMode.HalfPrecision);
            var receiver = (InterpolatedFieldBinding<Vector3>)ReplicatedFieldBindingFactory.Create(
                dst, typeof(Vector3), FieldBindingKind.PassiveInterpolated, 0, QuantizationMode.HalfPrecision);

            // Two snapshots 1s apart: (0,0,0) at t=1, (10,0,0) at t=2.
            SendSnapshot(sender, receiver, new Vector3(0f, 0f, 0f), receivedTime: 1.0);
            src.Value = new Vector3(10f, 0f, 0f);
            SendSnapshot(sender, receiver, new Vector3(10f, 0f, 0f), receivedTime: 2.0);

            receiver.TickRender(1.5);

            // Halfway between 0 and 10 is 5; half-float at magnitude ~10 is exact for
            // integers in this range, so tolerance is just the lerp accumulated error.
            Assert.AreEqual(5f, receiver.InterpolatedValue.x, 0.05f);
            Assert.AreEqual(0f, receiver.InterpolatedValue.y, 0.05f);
            Assert.AreEqual(0f, receiver.InterpolatedValue.z, 0.05f);
        }

        private static void SendSnapshot<T>(
            ReplicatedFieldBinding<T> sender,
            ReplicatedFieldBinding<T> receiver,
            T _,
            double receivedTime) where T : unmanaged
        {
            var writer = new FastBufferWriter(32, Allocator.Temp);
            try
            {
                sender.WriteTo(writer);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    receiver.ReadFrom(reader);
                    receiver.ApplyFromNetwork(receivedTime);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        // ---- Test fixtures ------------------------------------------------------

        private struct PackedStruct
        {
            public int A;
            public float B;
            public byte C;
        }
    }
}
