using Unity.Netcode;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Network codec for <see cref="EntityRef"/>. On the wire the ref travels as NGO's
    /// <see cref="NetworkObject.NetworkObjectId"/>, not <see cref="EntityId"/> — EntityId
    /// is a per-process monotonic counter (server's 100 is not client's 100), so shipping
    /// the raw value would resolve to an unrelated local entity on the receiver.
    /// NetworkObjectId is the only identity NGO guarantees to be in sync across peers.
    /// <para/>
    /// Not a <see cref="CodecRegistry"/> singleton: the codec holds an
    /// <see cref="IEntityRefResolver"/> that is per-<see cref="EntityReplicationSystem"/>.
    /// One instance is built lazily by the system
    /// (<see cref="EntityReplicationSystem.GetOrCreateEntityRefCodec"/>) and injected by
    /// <see cref="ReplicatedFieldBindingFactory"/> only for <see cref="EntityRef"/>-typed
    /// replicated fields.
    /// </summary>
    [Preserve]
    internal sealed class EntityRefCodec : IFieldCodec<EntityRef>
    {
        // Zero doubles as EntityRef.None and as "resolution failed" — NetworkObjectId 0
        // is never assigned by NGO to a spawned object, so the sentinel cannot collide
        // with a real reference on the wire.
        private const ulong NoRefSentinel = 0UL;

        private readonly IEntityRefResolver _resolver;

        public EntityRefCodec(IEntityRefResolver resolver) => _resolver = resolver;

        public int Size => sizeof(ulong);

        public void Write(FastBufferWriter writer, in EntityRef value)
        {
            if (value.IsNone)
            {
                writer.WriteValueSafe(NoRefSentinel);
                return;
            }

            // Resolve happens on the authority peer: server-auth fields are written on the
            // server, owner-auth fields on the owner. Both have the referenced entity
            // registered in their local _byEntityId — if they don't, the ref is dangling
            // (target never spawned here or despawned already) and we fall back to the
            // sentinel. Receiver observes EntityRef.None, which matches the intended
            // "reference can outlive its target" semantic of EntityRef.
            if (_resolver.TryResolveToNetworkObjectId(value.Id, out var networkObjectId))
            {
                writer.WriteValueSafe(networkObjectId);
                return;
            }

            writer.WriteValueSafe(NoRefSentinel);
        }

        public EntityRef Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);
            if (networkObjectId == NoRefSentinel) return EntityRef.None;

            // Race: the referenced entity may not be spawned yet on this peer (relevancy,
            // late-join, spawn-order). Don't buffer — return None and rely on the
            // resolve-each-time contract of EntityRef. The next StateBatch carrying this
            // field after the target spawns will re-write the ref and it will resolve.
            if (_resolver.TryResolveToEntityId(networkObjectId, out var id))
                return new EntityRef(id);

            return EntityRef.None;
        }
    }
}
