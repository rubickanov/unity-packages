using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    /// <summary>
    /// Covers the wire-format change (#1): every per-entity record in <c>ACS_StateBatch</c>
    /// carries a <c>ushort payloadBytes</c> prefix so records with an unknown
    /// <c>networkObjectId</c> can be skipped without dropping the batch tail.
    ///
    /// Exercises <see cref="EntityReplicationSystem.ApplyStateBatch"/> directly over a
    /// resolver-backed dictionary of reflection-built replicators — no NGO spawn, no
    /// NetworkManager. Mirrors the test scaffolding in <c>ApplyStateBufferRoundTripTests</c>.
    /// </summary>
    [TestFixture]
    public class StateBatchUnknownEntityTests
    {
        private const ulong IdA = 1;
        private const ulong IdB = 2;
        private const ulong IdUnknown = 9999;
        private const ulong IdUnknownOther = 12345;

        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            // Each test logs one warning per unknown-id record. Let the warning through
            // without failing the test.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // ---- Fixture helpers ----------------------------------------------------

        private EntityReplicator BuildReplicator(ReplicatedFieldBinding[] bindings, AuthorityMode[] authorities)
        {
            var go = new GameObject("StateBatchTest_" + _spawned.Count);
            _spawned.Add(go);
            go.AddComponent<NetworkObject>();
            var rep = go.AddComponent<EntityReplicator>();
            SetPrivate(rep, "_tickInterval", 0.05);
            SetPrivate(rep, "_bindings", bindings);
            SetPrivate(rep, "_bindingAuthorities", authorities);
            SetPrivate(rep, "_maskByteCount", (bindings.Length + 7) / 8);
            return rep;
        }

        private static void SetPrivate(object target, string name, object value)
        {
            var f = typeof(EntityReplicator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"EntityReplicator must have a private field '{name}' — rename detected?");
            f!.SetValue(target, value);
        }

        private static ReplicatedFieldBinding<T> MakeBinding<T>(ReactiveProperty<T> reactive) where T : unmanaged
        {
            return (ReplicatedFieldBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), FieldBindingKind.Plain);
        }

        /// <summary>
        /// Produces a record for one entity in the exact wire shape <see cref="EntityReplicationSystem.ApplyStateBatch"/>
        /// expects: <c>ulong id</c>, <c>ushort payloadBytes</c>, <c>int serverTick</c>,
        /// <c>byte[maskLen] mask</c>, then the field bytes in binding-index order.
        /// <paramref name="fieldsSize"/> is needed because <see cref="FastBufferWriter.Position"/>
        /// patching from outside is more fragile than up-front sizing in a tiny test helper.
        /// </summary>
        private readonly struct Record
        {
            public readonly ulong Id;
            public readonly int ServerTick;
            public readonly byte[] Mask;
            public readonly Action<FastBufferWriter> WriteFields;
            public readonly int FieldsSize;

            public Record(ulong id, int serverTick, byte[] mask, int fieldsSize, Action<FastBufferWriter> writeFields)
            {
                Id = id;
                ServerTick = serverTick;
                Mask = mask;
                FieldsSize = fieldsSize;
                WriteFields = writeFields;
            }

            public int PayloadBytes => sizeof(int) + Mask.Length + FieldsSize;
        }

        private static unsafe byte[] BuildBatch(params Record[] entities)
        {
            var w = new FastBufferWriter(1024, Allocator.Temp);
            try
            {
                w.WriteValueSafe((ushort)entities.Length);
                foreach (var rec in entities)
                {
                    w.WriteValueSafe(rec.Id);
                    w.WriteValueSafe((ushort)rec.PayloadBytes);
                    w.WriteValueSafe(rec.ServerTick);
                    fixed (byte* maskPtr = rec.Mask)
                        w.WriteBytesSafe(maskPtr, rec.Mask.Length);
                    rec.WriteFields(w);
                }
                return w.ToArray();
            }
            finally { w.Dispose(); }
        }

        private static unsafe void WriteFieldBytes<T>(FastBufferWriter w, T value) where T : unmanaged
        {
            w.WriteBytesSafe((byte*)&value, sizeof(T));
        }

        // ---- Tests --------------------------------------------------------------

        [Test]
        public void UnknownEntityMidBatch_KnownTail_TailApplied()
        {
            // Before the ushort payloadBytes prefix was added, hitting an unknown id in
            // the middle of the batch aborted the whole batch. This asserts the tail is
            // now reached and applied.
            var rA = new ReactiveProperty<int>(0);
            var rB = new ReactiveProperty<int>(0);
            var repA = BuildReplicator(new ReplicatedFieldBinding[] { MakeBinding(rA) },
                new[] { AuthorityMode.Server });
            var repB = BuildReplicator(new ReplicatedFieldBinding[] { MakeBinding(rB) },
                new[] { AuthorityMode.Server });

            // Synthetic unknown record — mask + fields matches what any 1-binding int
            // replicator would have serialized, so a naive reader that didn't use the
            // prefix could still walk it. The prefix-aware reader Seeks past it regardless.
            var batch = BuildBatch(
                new Record(IdA, 10, new byte[] { 0b01 }, sizeof(int), w => WriteFieldBytes(w, 42)),
                new Record(IdUnknown, 11, new byte[] { 0b01 }, sizeof(int), w => WriteFieldBytes(w, 999)),
                new Record(IdB, 12, new byte[] { 0b01 }, sizeof(int), w => WriteFieldBytes(w, 77)));

            var byId = new Dictionary<ulong, EntityReplicator> { [IdA] = repA, [IdB] = repB };
            Func<ulong, EntityReplicator> resolve = id => byId.TryGetValue(id, out var r) ? r : null;

            var reader = new FastBufferReader(batch, Allocator.Temp);
            try
            {
                EntityReplicationSystem.ApplyStateBatch(reader, resolve);
                Assert.AreEqual(reader.Length, reader.Position,
                    "reader must have fully consumed the batch — otherwise an under-read slipped through.");
            }
            finally { reader.Dispose(); }

            Assert.AreEqual(42, rA.Value, "known head record must apply");
            Assert.AreEqual(77, rB.Value, "known tail record must apply — the whole point of this fix");
        }

        [Test]
        public void TwoUnknownsThenKnown_KnownApplied()
        {
            var rA = new ReactiveProperty<int>(0);
            var repA = BuildReplicator(new ReplicatedFieldBinding[] { MakeBinding(rA) },
                new[] { AuthorityMode.Server });

            var batch = BuildBatch(
                new Record(IdUnknown, 1, new byte[] { 0b01 }, sizeof(int), w => WriteFieldBytes(w, 111)),
                new Record(IdUnknownOther, 2, new byte[] { 0b01 }, sizeof(int), w => WriteFieldBytes(w, 222)),
                new Record(IdA, 3, new byte[] { 0b01 }, sizeof(int), w => WriteFieldBytes(w, 55)));

            var byId = new Dictionary<ulong, EntityReplicator> { [IdA] = repA };
            Func<ulong, EntityReplicator> resolve = id => byId.TryGetValue(id, out var r) ? r : null;

            var reader = new FastBufferReader(batch, Allocator.Temp);
            try
            {
                EntityReplicationSystem.ApplyStateBatch(reader, resolve);
                Assert.AreEqual(reader.Length, reader.Position);
            }
            finally { reader.Dispose(); }

            Assert.AreEqual(55, rA.Value,
                "two unknowns in a row must not poison the reader — the known tail must still apply");
        }

        [Test]
        public void KnownThenUnknownTail_KnownApplied_NoOverread()
        {
            var rA = new ReactiveProperty<int>(0);
            var repA = BuildReplicator(new ReplicatedFieldBinding[] { MakeBinding(rA) },
                new[] { AuthorityMode.Server });

            // The unknown record has a different mask size than the known one — a reader
            // ignoring the prefix would desync here. The prefix forces a correct skip.
            var batch = BuildBatch(
                new Record(IdA, 7, new byte[] { 0b01 }, sizeof(int), w => WriteFieldBytes(w, 123)),
                new Record(IdUnknown, 8, new byte[] { 0xFF, 0xFF, 0xFF }, 12, w =>
                {
                    // Arbitrary bytes — content is irrelevant, only the Seek past matters.
                    WriteFieldBytes(w, 1); WriteFieldBytes(w, 2); WriteFieldBytes(w, 3);
                }));

            var byId = new Dictionary<ulong, EntityReplicator> { [IdA] = repA };
            Func<ulong, EntityReplicator> resolve = id => byId.TryGetValue(id, out var r) ? r : null;

            var reader = new FastBufferReader(batch, Allocator.Temp);
            try
            {
                EntityReplicationSystem.ApplyStateBatch(reader, resolve);
                Assert.AreEqual(reader.Length, reader.Position,
                    "reader must land exactly at end-of-batch, even when the tail record was unknown " +
                    "and had a different mask/field layout than the known record.");
            }
            finally { reader.Dispose(); }

            Assert.AreEqual(123, rA.Value, "known head record must apply");
        }
    }
}
