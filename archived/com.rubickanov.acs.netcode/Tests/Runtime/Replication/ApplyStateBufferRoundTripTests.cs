using System;
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
    public class ApplyStateBufferRoundTripTests
    {
        // ---- Fixture ------------------------------------------------------------
        //
        // EntityReplicator is a NetworkBehaviour — normally configured in OnNetworkSpawn
        // via NGO lifecycle. We bypass the lifecycle: AddComponent the replicator on a
        // bare GameObject with a NetworkObject, then set _bindings / _bindingAuthorities /
        // _tickInterval via reflection. ApplyStateBuffer only reads these three fields,
        // so it runs correctly with no NetworkManager or spawn.

        private GameObject _go;
        private EntityReplicator _replicator;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject(nameof(ApplyStateBufferRoundTripTests));
            _go.AddComponent<NetworkObject>();
            _replicator = _go.AddComponent<EntityReplicator>();
            SetPrivate(_replicator, "_tickInterval", 0.05);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private static void SetPrivate(object target, string name, object value)
        {
            var f = typeof(EntityReplicator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            // Meaningful precondition: if the runtime refactor renames these fields this
            // test suite must fail loudly, not silently apply to nothing.
            Assert.IsNotNull(f, $"EntityReplicator must have a private field '{name}' — rename detected?");
            f.SetValue(target, value);
        }

        private static ReplicatedFieldBinding<T> MakeBinding<T>(ReactiveProperty<T> reactive)
            where T : unmanaged
        {
            return (ReplicatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), FieldBindingKind.Plain);
        }

        private static ReplicatedFieldBinding<T> MakeBinding<T>(
            ReactiveProperty<T> reactive, QuantizationMode quantization)
            where T : unmanaged
        {
            return (ReplicatedFieldBinding<T>)ReplicatedFieldBindingFactory.Create(
                reactive, typeof(T), FieldBindingKind.Plain, 0, quantization);
        }

        private void SetBindings(ReplicatedFieldBinding[] bindings, AuthorityMode[] authorities)
        {
            SetPrivate(_replicator, "_bindings", bindings);
            SetPrivate(_replicator, "_bindingAuthorities", authorities);
            SetPrivate(_replicator, "_maskByteCount", (bindings.Length + 7) / 8);
        }

        /// <summary>
        /// Builds a state payload in the exact wire format <see cref="EntityReplicator.ApplyStateBuffer"/>
        /// expects: <c>int serverTick</c>, <c>byte[maskLen] dirtyMask</c>, then each dirty field's raw
        /// bytes in binding-index order.
        /// </summary>
        private static unsafe byte[] BuildPayload(int serverTick, byte[] dirtyMask, Action<FastBufferWriter> writeFields)
        {
            var w = new FastBufferWriter(256, Allocator.Temp);
            try
            {
                w.WriteValueSafe(serverTick);
                fixed (byte* ptr = dirtyMask)
                    w.WriteBytesSafe(ptr, dirtyMask.Length);
                writeFields(w);
                return w.ToArray();
            }
            finally { w.Dispose(); }
        }

        private static unsafe void WriteFieldBytes<T>(FastBufferWriter w, T value) where T : unmanaged
        {
            // Mirrors ReplicatedFieldBinding<T>.WriteTo exactly — raw memcpy of sizeof(T).
            w.WriteBytesSafe((byte*)&value, sizeof(T));
        }

        // ---- Tests --------------------------------------------------------------

        [Test]
        public void ApplyStateBuffer_FullMask_AllServerAuthBindingsUpdated()
        {
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<float>(0f);
            var bindingA = MakeBinding(rA);
            var bindingB = MakeBinding(rB);

            SetBindings(new ReplicatedFieldBinding[] { bindingA, bindingB },
                new[] { AuthorityMode.Server, AuthorityMode.Server });

            var payload = BuildPayload(serverTick: 10, dirtyMask: new byte[] { 0b11 }, w =>
            {
                WriteFieldBytes(w, 7);
                WriteFieldBytes(w, 3.14f);
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            Assert.AreEqual(7, rA.Value);
            Assert.AreEqual(3.14f, rB.Value, 0f);
        }

        [Test]
        public void ApplyStateBuffer_MaskBitClear_LeavesCorrespondingBindingUntouched()
        {
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<float>(-1f);
            var bindingA = MakeBinding(rA);
            var bindingB = MakeBinding(rB);

            SetBindings(new ReplicatedFieldBinding[] { bindingA, bindingB },
                new[] { AuthorityMode.Server, AuthorityMode.Server });

            // Mask = 0b01: only binding A is in the payload. Binding B's reactive must not move.
            var payload = BuildPayload(serverTick: 5, dirtyMask: new byte[] { 0b01 }, w =>
            {
                WriteFieldBytes(w, 42);
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            Assert.AreEqual(42, rA.Value);
            Assert.AreEqual(-1f, rB.Value, 0f);
        }

        [Test]
        public void ApplyStateBuffer_SkipOwnerAuth_ServerAppliedOwnerSkipped()
        {
            // bindingA = Server, bindingB = Owner. SkipOwnerAuth is what pure-client
            // owners pass in BroadcastStateRpc: their owner-auth state is fresher locally,
            // so the incoming owner bytes must be consumed (Skip) but NOT applied.
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<float>(888f);
            var bindingA = MakeBinding(rA);
            var bindingB = MakeBinding(rB);

            SetBindings(new ReplicatedFieldBinding[] { bindingA, bindingB },
                new[] { AuthorityMode.Server, AuthorityMode.Owner });

            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b11 }, w =>
            {
                WriteFieldBytes(w, 7);
                WriteFieldBytes(w, 3.14f);
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.SkipOwnerAuth);

            Assert.AreEqual(7, rA.Value, "server-auth binding must be applied");
            Assert.AreEqual(888f, rB.Value, 0f, "owner-auth binding must be skipped, not overwritten");
        }

        [Test]
        public void ApplyStateBuffer_ApplyAll_OwnerAuthAlsoApplied()
        {
            // Counterpart to SkipOwnerAuth: ApplyAll is the "no owner-auth protection"
            // mode — every field in the payload lands on its reactive regardless of
            // authority. Used by tests that do not need the owner-auth guard.
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<float>(888f);
            var bindingA = MakeBinding(rA);
            var bindingB = MakeBinding(rB);

            SetBindings(new ReplicatedFieldBinding[] { bindingA, bindingB },
                new[] { AuthorityMode.Server, AuthorityMode.Owner });

            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b11 }, w =>
            {
                WriteFieldBytes(w, 7);
                WriteFieldBytes(w, 3.14f);
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            Assert.AreEqual(7, rA.Value);
            Assert.AreEqual(3.14f, rB.Value, 0f);
        }

        [Test]
        public void ApplyStateBuffer_HighMaskBitOutsideBindingRange_DoesNotAffectLowBits_RegressionTwo()
        {
            // Regression #2: a set bit whose index >= _bindings.Length must not corrupt
            // lower-indexed bindings. The loop is bounded by _bindings.Length — bit 63 is
            // never visited, so it must be silently ignored while bit 0 is still applied.
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<float>(-7f);
            var bindingA = MakeBinding(rA);
            var bindingB = MakeBinding(rB);

            SetBindings(new ReplicatedFieldBinding[] { bindingA, bindingB },
                new[] { AuthorityMode.Server, AuthorityMode.Server });

            // Bit 0 set + bit 7 set. With only 2 bindings, bit 7 is outside the binding range
            // but inside the 1-byte mask — the loop bounded by _bindings.Length must ignore it.
            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b10000001 }, w =>
            {
                WriteFieldBytes(w, 42);
                // Intentionally no bytes for bit 7 — the loop must not try to read them.
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            Assert.AreEqual(42, rA.Value, "bit-0 field must be applied");
            Assert.AreEqual(-7f, rB.Value, 0f, "bit-1 field must stay untouched (bit was clear)");
        }

        // ---- SkipOwnerAuthIfLocallyWritten (regression #19) ---------------------

        [Test]
        public void ApplyStateBuffer_SkipOwnerAuthIfLocallyWritten_OwnerNotWritten_OwnerAuthApplied()
        {
            // #19 path A: the owner-auth binding's OwnerWroteSinceSpawn flag is false
            // (default — pure-client owner has not touched the field yet), so the
            // initial-sync snapshot MUST deliver the server-preset value. Without this,
            // owner-only fields like WeaponId would stay at default(T) forever.
            var rServer = new ReactiveProperty<int>(0);
            var rOwner = new ReactiveProperty<float>(0f);
            var bindingServer = MakeBinding(rServer);
            var bindingOwner = MakeBinding(rOwner);

            SetBindings(new ReplicatedFieldBinding[] { bindingServer, bindingOwner },
                new[] { AuthorityMode.Server, AuthorityMode.Owner });

            Assert.IsFalse(bindingOwner.OwnerWroteSinceSpawn,
                "precondition: default flag on a fresh binding must be false");

            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b11 }, w =>
            {
                WriteFieldBytes(w, 7);
                WriteFieldBytes(w, 3.14f);
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.SkipOwnerAuthIfLocallyWritten);

            Assert.AreEqual(7, rServer.Value, "server-auth field must always apply");
            Assert.AreEqual(3.14f, rOwner.Value, 0f,
                "owner-auth field with OwnerWroteSinceSpawn=false must apply — this is the " +
                "permanent-default-avoidance path.");
        }

        [Test]
        public void ApplyStateBuffer_SkipOwnerAuthIfLocallyWritten_OwnerWritten_OwnerAuthSkipped()
        {
            // #19 path B: the owner has already produced a local write between sending
            // RequestInitialStateRpc and receiving SendInitialStateRpc, so
            // OwnerWroteSinceSpawn is true. The incoming server snapshot must NOT
            // overwrite that fresh local value. Server-auth still applies.
            //
            // Note: rOwner initial value must differ from the post-reset write value —
            // R3 ReactiveProperty dedupes equal assignments, so `new (42f)` + `Value = 42f`
            // would skip the subscribe callback and leave OwnerWroteSinceSpawn == false.
            var rServer = new ReactiveProperty<int>(0);
            var rOwner = new ReactiveProperty<float>(0f);
            var bindingServer = MakeBinding(rServer);
            var bindingOwner = MakeBinding(rOwner);

            SetBindings(new ReplicatedFieldBinding[] { bindingServer, bindingOwner },
                new[] { AuthorityMode.Server, AuthorityMode.Owner });

            // Simulate the local authority write: subscribe + reset + mutate. The subscribe
            // replay would flip the flag synthetically, so the reset mirrors what
            // OnNetworkSpawn does, and the explicit write is what flips it "for real".
            var bag = new DisposableBag();
            try
            {
                bindingOwner.SubscribeAsAuthority(ref bag);
                bindingOwner.ResetOwnerWroteSinceSpawn();   // simulate what OnNetworkSpawn does
                rOwner.Value = 42f;                          // fresh local write (0f → 42f)
                Assert.IsTrue(bindingOwner.OwnerWroteSinceSpawn,
                    "precondition: authority-side write after reset must set the flag");

                var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b11 }, w =>
                {
                    WriteFieldBytes(w, 7);
                    WriteFieldBytes(w, 999f);   // stale server value — must be discarded
                });

                _replicator.ApplyStateBuffer(payload, StateApplyMode.SkipOwnerAuthIfLocallyWritten);

                Assert.AreEqual(7, rServer.Value,
                    "server-auth field must apply regardless of any owner flag state");
                Assert.AreEqual(42f, rOwner.Value, 0f,
                    "owner-auth field with OwnerWroteSinceSpawn=true must be preserved, not overwritten");
            }
            finally { bag.Dispose(); }
        }

        [Test]
        public void ApplyStateBuffer_SkipOwnerAuthIfLocallyWritten_ServerAuthIgnoresFlag()
        {
            // Short-circuit test: the skip decision is gated by
            // `_bindingAuthorities[i] == Owner`, so a server-auth binding must apply
            // even if its OwnerWroteSinceSpawn somehow became true (e.g. because the
            // server peer also subscribes as authority and the subscribe-replay set it).
            // This guards against accidentally changing the AND to an OR in the skip
            // predicate — without the short-circuit, server-auth fields would silently
            // stop replicating after the first tick on the server.
            var rServerA = new ReactiveProperty<int>(0);
            var rServerB = new ReactiveProperty<float>(-1f);
            var bindingA = MakeBinding(rServerA);
            var bindingB = MakeBinding(rServerB);

            SetBindings(new ReplicatedFieldBinding[] { bindingA, bindingB },
                new[] { AuthorityMode.Server, AuthorityMode.Server });

            // Set the flag on both bindings by subscribing (R3 replay flips it).
            var bag = new DisposableBag();
            try
            {
                bindingA.SubscribeAsAuthority(ref bag);
                bindingB.SubscribeAsAuthority(ref bag);
                Assert.IsTrue(bindingA.OwnerWroteSinceSpawn, "precondition: flag set via subscribe replay");
                Assert.IsTrue(bindingB.OwnerWroteSinceSpawn, "precondition: flag set via subscribe replay");

                var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b11 }, w =>
                {
                    WriteFieldBytes(w, 55);
                    WriteFieldBytes(w, 66.6f);
                });

                _replicator.ApplyStateBuffer(payload, StateApplyMode.SkipOwnerAuthIfLocallyWritten);

                Assert.AreEqual(55, rServerA.Value,
                    "server-auth field must apply even when OwnerWroteSinceSpawn is set");
                Assert.AreEqual(66.6f, rServerB.Value, 0f,
                    "server-auth field must apply even when OwnerWroteSinceSpawn is set");
            }
            finally { bag.Dispose(); }
        }

        // ---- Quantization e2e ---------------------------------------------------

        [Test]
        public void ApplyStateBuffer_QuantizedVector3_AppliedWithinHalfPrecisionTolerance()
        {
            // End-to-end through the real pipeline: sender encodes via FloatHalfCodec×3 (6B
            // on wire instead of 12B), payload is laid out exactly as EntityReplicationSystem
            // would lay it out, ApplyStateBuffer reads through the receiver's codec and lands
            // the value on the reactive within half-float tolerance. This proves the codec
            // selection survives the full path and the wire shrinks as advertised.
            var sender = new ReactiveProperty<Vector3>(new Vector3(12.5f, -7.25f, 0.125f));
            var receiver = new ReactiveProperty<Vector3>(Vector3.zero);
            var senderBinding = MakeBinding(sender, QuantizationMode.HalfPrecision);
            var receiverBinding = MakeBinding(receiver, QuantizationMode.HalfPrecision);

            SetBindings(new ReplicatedFieldBinding[] { receiverBinding },
                new[] { AuthorityMode.Server });

            // Ask the sender's binding to write the field into the payload. Bytes written
            // == codec.Size (6). If the factory threaded RawCodec by mistake we'd write 12
            // and ApplyStateBuffer would mis-align on later fields.
            int writtenBytes = 0;
            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b1 }, w =>
            {
                int before = w.Position;
                senderBinding.WriteTo(w);
                writtenBytes = w.Position - before;
            });

            Assert.AreEqual(6, writtenBytes,
                "sender must have written 6 bytes (HalfPrecision Vector3) — if 12, codec wasn't applied");

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            // Half-float at magnitude ~10 has ~0.01 quantization error; 0.05 is a safe margin.
            Assert.AreEqual(12.5f, receiver.Value.x, 0.05f);
            Assert.AreEqual(-7.25f, receiver.Value.y, 0.05f);
            Assert.AreEqual(0.125f, receiver.Value.z, 0.05f);
        }

        [Test]
        public void ApplyStateBuffer_QuantizedQuaternion_AppliedWithinSmallestThreeTolerance()
        {
            // Same e2e idea for Quaternion → SmallestThree (4B on wire instead of 16B).
            // Tolerance is angular (dot ≥ 0.999 ≈ ~1° error) — the documented bound.
            var input = Quaternion.Euler(30f, 60f, 90f);
            var sender = new ReactiveProperty<Quaternion>(input);
            var receiver = new ReactiveProperty<Quaternion>(Quaternion.identity);
            var senderBinding = MakeBinding(sender, QuantizationMode.SmallestThree);
            var receiverBinding = MakeBinding(receiver, QuantizationMode.SmallestThree);

            SetBindings(new ReplicatedFieldBinding[] { receiverBinding },
                new[] { AuthorityMode.Server });

            int writtenBytes = 0;
            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b1 }, w =>
            {
                int before = w.Position;
                senderBinding.WriteTo(w);
                writtenBytes = w.Position - before;
            });

            Assert.AreEqual(4, writtenBytes,
                "sender must have written 4 bytes (SmallestThree Quaternion) — if 16, codec wasn't applied");

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            float dot = Mathf.Abs(Quaternion.Dot(input, receiver.Value));
            Assert.GreaterOrEqual(dot, 0.999f, $"angular error too large: dot={dot}");
        }

        [Test]
        public void ApplyStateBuffer_MixedQuantizedAndRawFields_RoutedCorrectlyByPerFieldSize()
        {
            // Hardest e2e: three bindings of mixed codecs in one payload — quantized Vec3 (6B)
            // + raw int (4B) + quantized Quat (4B) = 14B total. If any codec lies about its
            // Size, the next field's read mis-aligns and decodes garbage. This is the test
            // most likely to catch a Size/Read mismatch in any codec.
            var rVec = new ReactiveProperty<Vector3>(Vector3.zero);
            var rInt = new ReactiveProperty<int>(0);
            var rQuat = new ReactiveProperty<Quaternion>(Quaternion.identity);

            var inputVec = new Vector3(3f, 4f, 5f);
            var inputQuat = Quaternion.AngleAxis(45f, Vector3.up);

            var sVec = new ReactiveProperty<Vector3>(inputVec);
            var sInt = new ReactiveProperty<int>(0x12345678);
            var sQuat = new ReactiveProperty<Quaternion>(inputQuat);
            var senderVec = MakeBinding(sVec, QuantizationMode.HalfPrecision);
            var senderInt = MakeBinding(sInt);                                  // raw — control
            var senderQuat = MakeBinding(sQuat, QuantizationMode.SmallestThree);

            var bVec = MakeBinding(rVec, QuantizationMode.HalfPrecision);
            var bInt = MakeBinding(rInt);
            var bQuat = MakeBinding(rQuat, QuantizationMode.SmallestThree);

            SetBindings(new ReplicatedFieldBinding[] { bVec, bInt, bQuat },
                new[] { AuthorityMode.Server, AuthorityMode.Server, AuthorityMode.Server });

            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b111 }, w =>
            {
                senderVec.WriteTo(w);
                senderInt.WriteTo(w);
                senderQuat.WriteTo(w);
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            // Vec3 within half tolerance.
            Assert.AreEqual(inputVec.x, rVec.Value.x, 0.05f);
            Assert.AreEqual(inputVec.y, rVec.Value.y, 0.05f);
            Assert.AreEqual(inputVec.z, rVec.Value.z, 0.05f);
            // Int bit-exact (raw codec, 4B).
            Assert.AreEqual(0x12345678, rInt.Value);
            // Quat angular tolerance.
            float dot = Mathf.Abs(Quaternion.Dot(inputQuat, rQuat.Value));
            Assert.GreaterOrEqual(dot, 0.999f, $"angular error too large: dot={dot}");
        }

        [Test]
        public void ApplyStateBuffer_SkipOwnerAuthOnQuantizedField_AdvancesByCodecSizeNotSizeofT()
        {
            // Skip path semantics: ApplyStateBuffer calls Skip(reader) only when a field IS
            // in the payload (bit set) but is owner-auth and the mode says skip — used by
            // pure-client owners who already have a fresher local value.
            //
            // The reader MUST advance by codec.Size, not sizeof(T). With a quantized Vec3,
            // sizeof(T)=12 but Size=6; if Skip walked sizeof(T), the reader would jump 6B
            // past the Vec3 into the next field, and the next field's read would return
            // garbage. Here: owner-auth quantized Vec3 + server-auth int, both masked dirty,
            // mode=SkipOwnerAuth → Vec3 skipped (6B), int applied (4B from offset 6).
            var rVec = new ReactiveProperty<Vector3>(new Vector3(99f, 99f, 99f));
            var rInt = new ReactiveProperty<int>(0);
            var bVec = MakeBinding(rVec, QuantizationMode.HalfPrecision);
            var bInt = MakeBinding(rInt);

            SetBindings(new ReplicatedFieldBinding[] { bVec, bInt },
                new[] { AuthorityMode.Owner, AuthorityMode.Server });

            var sVec = new ReactiveProperty<Vector3>(new Vector3(1f, 2f, 3f));
            var sInt = new ReactiveProperty<int>(777);
            var senderVec = MakeBinding(sVec, QuantizationMode.HalfPrecision);
            var senderInt = MakeBinding(sInt);

            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b11 }, w =>
            {
                senderVec.WriteTo(w);   // 6 bytes that Skip must consume
                senderInt.WriteTo(w);   // 4 bytes that must land on rInt
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.SkipOwnerAuth);

            Assert.AreEqual(new Vector3(99f, 99f, 99f), rVec.Value,
                "owner-auth field with SkipOwnerAuth must NOT be applied");
            Assert.AreEqual(777, rInt.Value,
                "if Skip used sizeof(T)=12 instead of codec.Size=6, the int read would be misaligned");
        }

        [Test]
        public void ApplyStateBuffer_MixedBindingTypes_EachByteBlockRoutedToCorrectField()
        {
            // Three bindings of different sizes — if the reader miscounts bytes on any of
            // them, subsequent fields will deserialize into garbage.
            var rInt = new ReactiveProperty<int>(0);
            var rVec = new ReactiveProperty<Vector3>(Vector3.zero);
            var rBool = new ReactiveProperty<bool>(false);
            var bInt = MakeBinding(rInt);
            var bVec = MakeBinding(rVec);
            var bBool = MakeBinding(rBool);

            SetBindings(new ReplicatedFieldBinding[] { bInt, bVec, bBool },
                new[] { AuthorityMode.Server, AuthorityMode.Server, AuthorityMode.Server });

            var expectedVec = new Vector3(1.5f, -2.5f, 3.5f);
            var payload = BuildPayload(serverTick: 1, dirtyMask: new byte[] { 0b111 }, w =>
            {
                WriteFieldBytes(w, 0x11223344);
                WriteFieldBytes(w, expectedVec);
                WriteFieldBytes(w, true);
            });

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            Assert.AreEqual(0x11223344, rInt.Value);
            Assert.AreEqual(expectedVec.x, rVec.Value.x, 0f);
            Assert.AreEqual(expectedVec.y, rVec.Value.y, 0f);
            Assert.AreEqual(expectedVec.z, rVec.Value.z, 0f);
            Assert.IsTrue(rBool.Value);
        }
    }
}
