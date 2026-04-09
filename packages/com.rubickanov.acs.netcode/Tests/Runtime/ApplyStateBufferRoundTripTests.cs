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
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), interpolate: false);
        }

        /// <summary>
        /// Builds a state payload in the exact wire format <see cref="AspectReplicator.ApplyStateBuffer"/>
        /// expects: <c>int serverTick</c>, <c>ulong dirtyMask</c>, then each dirty field's raw bytes
        /// in binding-index order.
        /// </summary>
        private static byte[] BuildPayload(int serverTick, ulong dirtyMask, Action<FastBufferWriter> writeFields)
        {
            var w = new FastBufferWriter(256, Allocator.Temp);
            try
            {
                w.WriteValueSafe(serverTick);
                w.WriteValueSafe(dirtyMask);
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

            SetPrivate(_replicator, "_bindings", new ReplicatedFieldBinding[] { bindingA, bindingB });
            SetPrivate(_replicator, "_bindingAuthorities",
                new[] { AuthorityMode.Server, AuthorityMode.Server });

            var payload = BuildPayload(serverTick: 10, dirtyMask: 0b11UL, w =>
            {
                WriteFieldBytes(w, 7);
                WriteFieldBytes(w, 3.14f);
            });

            _replicator.ApplyStateBuffer(payload, skipOwnerFields: false);

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

            SetPrivate(_replicator, "_bindings", new ReplicatedFieldBinding[] { bindingA, bindingB });
            SetPrivate(_replicator, "_bindingAuthorities",
                new[] { AuthorityMode.Server, AuthorityMode.Server });

            // Mask = 0b01: only binding A is in the payload. Binding B's reactive must not move.
            var payload = BuildPayload(serverTick: 5, dirtyMask: 0b01UL, w =>
            {
                WriteFieldBytes(w, 42);
            });

            _replicator.ApplyStateBuffer(payload, skipOwnerFields: false);

            Assert.AreEqual(42, rA.Value);
            Assert.AreEqual(-1f, rB.Value, 0f);
        }

        [Test]
        public void ApplyStateBuffer_SkipOwnerFieldsTrue_ServerAppliedOwnerSkipped()
        {
            // bindingA = Server, bindingB = Owner. skipOwnerFields=true is what pure-client
            // owners pass in BroadcastStateRpc: their owner-auth state is fresher locally,
            // so the incoming owner bytes must be consumed (Skip) but NOT applied.
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<float>(888f);
            var bindingA = MakeBinding(rA);
            var bindingB = MakeBinding(rB);

            SetPrivate(_replicator, "_bindings", new ReplicatedFieldBinding[] { bindingA, bindingB });
            SetPrivate(_replicator, "_bindingAuthorities",
                new[] { AuthorityMode.Server, AuthorityMode.Owner });

            var payload = BuildPayload(serverTick: 1, dirtyMask: 0b11UL, w =>
            {
                WriteFieldBytes(w, 7);
                WriteFieldBytes(w, 3.14f);
            });

            _replicator.ApplyStateBuffer(payload, skipOwnerFields: true);

            Assert.AreEqual(7, rA.Value, "server-auth binding must be applied");
            Assert.AreEqual(888f, rB.Value, 0f, "owner-auth binding must be skipped, not overwritten");
        }

        [Test]
        public void ApplyStateBuffer_SkipOwnerFieldsFalse_OwnerAuthAlsoApplied()
        {
            // Counterpart to the previous test: SendInitialStateRpc on pure-client owners
            // passes skipOwnerFields=false precisely so owner-auth fields WILL be applied
            // (e.g. pre-set WeaponId). This asserts that contract.
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<float>(888f);
            var bindingA = MakeBinding(rA);
            var bindingB = MakeBinding(rB);

            SetPrivate(_replicator, "_bindings", new ReplicatedFieldBinding[] { bindingA, bindingB });
            SetPrivate(_replicator, "_bindingAuthorities",
                new[] { AuthorityMode.Server, AuthorityMode.Owner });

            var payload = BuildPayload(serverTick: 1, dirtyMask: 0b11UL, w =>
            {
                WriteFieldBytes(w, 7);
                WriteFieldBytes(w, 3.14f);
            });

            _replicator.ApplyStateBuffer(payload, skipOwnerFields: false);

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

            SetPrivate(_replicator, "_bindings", new ReplicatedFieldBinding[] { bindingA, bindingB });
            SetPrivate(_replicator, "_bindingAuthorities",
                new[] { AuthorityMode.Server, AuthorityMode.Server });

            ulong mask = (1UL << 0) | (1UL << 63);
            var payload = BuildPayload(serverTick: 1, dirtyMask: mask, w =>
            {
                WriteFieldBytes(w, 42);
                // Intentionally no bytes for bit 63 — the loop must not try to read them.
            });

            _replicator.ApplyStateBuffer(payload, skipOwnerFields: false);

            Assert.AreEqual(42, rA.Value, "bit-0 field must be applied");
            Assert.AreEqual(-7f, rB.Value, 0f, "bit-1 field must stay untouched (bit was clear)");
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

            SetPrivate(_replicator, "_bindings",
                new ReplicatedFieldBinding[] { bInt, bVec, bBool });
            SetPrivate(_replicator, "_bindingAuthorities",
                new[] { AuthorityMode.Server, AuthorityMode.Server, AuthorityMode.Server });

            var expectedVec = new Vector3(1.5f, -2.5f, 3.5f);
            var payload = BuildPayload(serverTick: 1, dirtyMask: 0b111UL, w =>
            {
                WriteFieldBytes(w, 0x11223344);
                WriteFieldBytes(w, expectedVec);
                WriteFieldBytes(w, true);
            });

            _replicator.ApplyStateBuffer(payload, skipOwnerFields: false);

            Assert.AreEqual(0x11223344, rInt.Value);
            Assert.AreEqual(expectedVec.x, rVec.Value.x, 0f);
            Assert.AreEqual(expectedVec.y, rVec.Value.y, 0f);
            Assert.AreEqual(expectedVec.z, rVec.Value.z, 0f);
            Assert.IsTrue(rBool.Value);
        }
    }
}
