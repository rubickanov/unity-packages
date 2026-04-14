namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Bidirectional translation surface between the local <see cref="EntityId"/> space and
    /// NGO's network-synchronized <see cref="NetworkObjectId"/>. <see cref="EntityRefCodec"/>
    /// depends on this interface instead of <see cref="AspectReplicationSystem"/> so tests
    /// can plug in a fake without standing up a full NetworkManager. Both methods return
    /// false when the entity is unknown, despawned, or has no matching replicator — the
    /// codec treats that as "no reference" and writes the sentinel.
    /// </summary>
    internal interface IEntityRefResolver
    {
        bool TryResolveToNetworkObjectId(EntityId id, out ulong networkObjectId);
        bool TryResolveToEntityId(ulong networkObjectId, out EntityId id);
    }
}
