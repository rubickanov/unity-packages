using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    /// <summary>
    /// Verifies that <see cref="EntityRefCodec"/> ships NetworkObjectId on the wire — not
    /// raw EntityId — so a ref written on the sender resolves to the correct peer-local
    /// entity on the receiver. Edge cases cover None, dangling source, and unknown target
    /// (late spawn / relevancy race).
    /// </summary>
    [TestFixture]
    public class EntityRefCodecTests
    {
        /// <summary>
        /// Minimal <see cref="IEntityRefResolver"/> stub for isolated codec testing. Models
        /// one peer's local view: a bidirectional dictionary between EntityId.Value and
        /// NetworkObjectId. Tests build two resolvers with different EntityId ↔ NetworkObjectId
        /// mappings to confirm the codec translates through the synchronized identity.
        /// </summary>
        private sealed class FakeResolver : IEntityRefResolver
        {
            private readonly Dictionary<ulong, ulong> _entityToNet = new();
            private readonly Dictionary<ulong, ulong> _netToEntity = new();

            public void Map(EntityId entityId, ulong networkObjectId)
            {
                _entityToNet[entityId.Value] = networkObjectId;
                _netToEntity[networkObjectId] = entityId.Value;
            }

            public bool TryResolveToNetworkObjectId(EntityId id, out ulong networkObjectId)
                => _entityToNet.TryGetValue(id.Value, out networkObjectId);

            public bool TryResolveToEntityId(ulong networkObjectId, out EntityId id)
            {
                if (_netToEntity.TryGetValue(networkObjectId, out var raw))
                {
                    id = new EntityId(raw);
                    return true;
                }
                id = EntityId.None;
                return false;
            }
        }

        private static EntityRef RoundTrip(IEntityRefResolver sender, IEntityRefResolver receiver, EntityRef value, out int bytesWritten)
        {
            var senderCodec = new EntityRefCodec(sender);
            var receiverCodec = new EntityRefCodec(receiver);

            var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp);
            try
            {
                int before = writer.Position;
                senderCodec.Write(writer, value);
                bytesWritten = writer.Position - before;

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    return receiverCodec.Read(reader);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        [Test]
        public void Size_IsEightBytes_NetworkObjectIdWidth()
        {
            // Matches the NetworkObjectId width on the wire. Regression guard: the
            // per-entity payload size prefix in ACS_StateBatch depends on this value.
            var codec = new EntityRefCodec(new FakeResolver());
            Assert.AreEqual(sizeof(ulong), codec.Size);
        }

        [Test]
        public void Write_Then_Read_Roundtrips_Reference_Through_NetworkObjectId_Translation()
        {
            // The whole point of the codec: sender's EntityId space differs from receiver's,
            // but both agree on NetworkObjectId. The ref must land on the correct local entity.
            var sender = new FakeResolver();
            sender.Map(new EntityId(100), networkObjectId: 7);

            var receiver = new FakeResolver();
            receiver.Map(new EntityId(42), networkObjectId: 7);

            var result = RoundTrip(sender, receiver, new EntityRef(new EntityId(100)), out int bytes);

            Assert.AreEqual(sizeof(ulong), bytes);
            Assert.AreEqual(new EntityId(42), result.Id,
                "Codec must translate sender's EntityId 100 → NetworkObjectId 7 → receiver's EntityId 42");
        }

        [Test]
        public void Write_EntityRefNone_Reads_As_None()
        {
            // Sentinel round-trip: None travels as NetworkObjectId 0 regardless of resolver state.
            var sender = new FakeResolver();
            var receiver = new FakeResolver();

            var result = RoundTrip(sender, receiver, EntityRef.None, out _);

            Assert.IsTrue(result.IsNone);
        }

        [Test]
        public void Write_DanglingRef_Reads_As_None()
        {
            // Sender has no mapping for the EntityId (target despawned or never spawned on
            // this peer). Codec writes the sentinel; receiver sees None. Same observable
            // behaviour as a locally-destroyed target.
            var sender = new FakeResolver();
            // Intentionally no sender.Map — the ref is dangling at source.

            var receiver = new FakeResolver();
            receiver.Map(new EntityId(42), networkObjectId: 7);

            var result = RoundTrip(sender, receiver, new EntityRef(new EntityId(999)), out _);

            Assert.IsTrue(result.IsNone,
                "Dangling source ref must be encoded as the None sentinel, not raw EntityId bytes");
        }

        [Test]
        public void Read_UnknownNetworkObjectId_Returns_None()
        {
            // Race: replicated state arrives before the target spawns on this peer
            // (relevancy, late-join, spawn-order). Receiver returns None and relies on the
            // next StateBatch after spawn to re-deliver the ref.
            var sender = new FakeResolver();
            sender.Map(new EntityId(100), networkObjectId: 7);

            var receiver = new FakeResolver();
            // Intentionally no receiver.Map — target not yet spawned here.

            var result = RoundTrip(sender, receiver, new EntityRef(new EntityId(100)), out _);

            Assert.IsTrue(result.IsNone);
        }

        [Test]
        public void Write_Uses_NetworkObjectId_Not_RawEntityId()
        {
            // Direct wire inspection: the single ulong in the stream must be the resolver's
            // NetworkObjectId, not the raw EntityId.Value. Catches regressions that
            // accidentally fall back to a RawCodec-style memcpy of the EntityRef struct.
            var sender = new FakeResolver();
            sender.Map(new EntityId(100), networkObjectId: 7);

            var codec = new EntityRefCodec(sender);
            var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp);
            try
            {
                codec.Write(writer, new EntityRef(new EntityId(100)));

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    reader.ReadValueSafe(out ulong onWire);
                    Assert.AreEqual(7UL, onWire,
                        "Wire payload must be the NetworkObjectId, not the raw EntityId.Value");
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }
    }
}
