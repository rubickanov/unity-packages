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
        // AspectReplicator is a NetworkBehaviour — normally configured in OnNetworkSpawn
        // via NGO lifecycle. We bypass the lifecycle: AddComponent the replicator on a
        // bare GameObject with a NetworkObject, then set _bindings / _bindingAuthorities /
        // _tickInterval via reflection. ApplyStateBuffer only reads these three fields,
        // so it runs correctly with no NetworkManager or spawn.

        private GameObject _go;
        private AspectReplicator _replicator;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject(nameof(ApplyStateBufferRoundTripTests));
            _go.AddComponent<NetworkObject>();
            _replicator = _go.AddComponent<AspectReplicator>();
            SetPrivate(_replicator, "_tickInterval", 0.05);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private static void SetPrivate(object target, string name, object value)
        {
            var f = typeof(AspectReplicator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            // Meaningful precondition: if the runtime refactor renames these fields this
            // test suite must fail loudly, not silently apply to nothing.
            Assert.IsNotNull(f, $"AspectReplicator must have a private field '{name}' — rename detected?");
            f.SetValue(target, value);
        }

        private static ReplicatedFieldBinding<T> MakeBinding<T>(ReactiveProperty<T> reactive)
            where T : unmanaged
        {
            return (ReplicatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), FieldBindingKind.Plain);
        }

        private void SetBindings(ReplicatedFieldBinding[] bindings, AuthorityMode[] authorities)
        {
            SetPrivate(_replicator, "_bindings", bindings);
            SetPrivate(_replicator, "_bindingAuthorities", authorities);
            SetPrivate(_replicator, "_maskByteCount", (bindings.Length + 7) / 8);
        }

        /// <summary>
        /// Builds a state payload in the exact wire format <see cref="AspectReplicator.ApplyStateBuffer"/>
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
