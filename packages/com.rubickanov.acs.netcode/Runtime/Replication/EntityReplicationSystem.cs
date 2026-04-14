using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Centralized replication system that collects dirty state from all registered
    /// EntityReplicators and sends one batched message per tick via CustomMessagingManager.
    /// Eliminates per-entity tick subscriptions (#10), per-tick byte[] allocations (#6),
    /// per-event byte[] allocations (#7), managed→native copies (#8), and per-entity
    /// broadcaster delegates (#11).
    /// </summary>
    internal sealed class EntityReplicationSystem : IDisposable, IEventBroadcaster, IEntityRefResolver
    {
        private const string StateBatchChannel = "ACS_StateBatch";
        private const string OwnerSubmitChannel = "ACS_OwnerSubmit";
        private const string EventBroadcastChannel = "ACS_EventBcast";
        private const string EventBroadcastUnreliableChannel = "ACS_EventBcastU";
        private const string OwnerEventChannel = "ACS_OwnerEvt";
        private const string OwnerEventUnreliableChannel = "ACS_OwnerEvtU";
        private const string SyncRequestChannel = "ACS_SyncReq";
        private const string SyncReplyChannel = "ACS_SyncReply";

        private static readonly Dictionary<NetworkManager, EntityReplicationSystem> s_Systems = new();

        // Play-Mode-without-Domain-Reload safety: static dictionaries otherwise survive
        // stop→play cycles with stale NetworkManager keys from the previous session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Systems.Clear();
        }

        private readonly NetworkManager _networkManager;
        private readonly Dictionary<ulong, EntityReplicator> _byNetworkObjectId = new();
        // Secondary index keyed by EntityId.Value. Populated alongside _byNetworkObjectId so
        // EntityRefCodec can translate local EntityId → NetworkObjectId in O(1) on the write
        // path without reaching into World/MonoEntity from a hot serialization loop. Also
        // primes the system for future EntityId-addressed APIs (RPCs, snapshot replay,
        // relevancy debug tools). EntityId.None (value 0) is never inserted — an entity with
        // no id would collide with the sentinel the codec uses for "no reference".
        private readonly Dictionary<ulong, EntityReplicator> _byEntityId = new();
        private readonly List<EntityReplicator> _replicators = new();
        private EntityReplicator[] _iterationSnapshot = Array.Empty<EntityReplicator>();
        private bool _snapshotDirty;
        private readonly List<ulong> _broadcastTargetIds = new();
        private readonly List<EntityReplicator> _dirtyReplicatorsBuffer = new();
        private bool _broadcastTargetsDirty = true;
        private bool _disposed;

        // Cached once so OnStateBatchReceived does not allocate a closure per tick. The
        // lambda captures _byNetworkObjectId; a null return signals "unknown or despawned",
        // which tells ApplyStateBatch to Seek past the record and continue the batch tail.
        private readonly Func<ulong, EntityReplicator?> _resolveSpawned;

        // One codec instance per system — EntityRefCodec holds a resolver reference, so it
        // cannot be a CodecRegistry singleton (resolver is per-NetworkManager). Built
        // lazily because most aspects have no EntityRef-typed [Replicated] fields.
        private EntityRefCodec? _entityRefCodec;

        internal EntityRefCodec GetOrCreateEntityRefCodec()
            => _entityRefCodec ??= new EntityRefCodec(this);

        private EntityReplicationSystem(NetworkManager networkManager)
        {
            _networkManager = networkManager;
            _resolveSpawned = id =>
                _byNetworkObjectId.TryGetValue(id, out var r) && r.IsSpawned ? r : null;

            networkManager.NetworkTickSystem.Tick += OnTick;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;

            var messaging = networkManager.CustomMessagingManager;
            messaging.RegisterNamedMessageHandler(StateBatchChannel, OnStateBatchReceived);
            messaging.RegisterNamedMessageHandler(OwnerSubmitChannel, OnOwnerSubmitReceived);
            messaging.RegisterNamedMessageHandler(EventBroadcastChannel, OnEventBroadcastReceived);
            messaging.RegisterNamedMessageHandler(EventBroadcastUnreliableChannel, OnEventBroadcastReceived);
            messaging.RegisterNamedMessageHandler(OwnerEventChannel, OnOwnerEventReceived);
            messaging.RegisterNamedMessageHandler(OwnerEventUnreliableChannel, OnOwnerEventReceived);
            messaging.RegisterNamedMessageHandler(SyncRequestChannel, OnSyncRequestReceived);
            messaging.RegisterNamedMessageHandler(SyncReplyChannel, OnSyncReplyReceived);
        }

        internal static EntityReplicationSystem GetOrCreate(NetworkManager networkManager)
        {
            if (!s_Systems.TryGetValue(networkManager, out var system))
            {
                system = new EntityReplicationSystem(networkManager);
                s_Systems[networkManager] = system;
            }
            return system;
        }

        // Exposed for tests that need to verify cleanup.
        internal static bool TryGet(NetworkManager networkManager, out EntityReplicationSystem system)
        {
            return s_Systems.TryGetValue(networkManager, out system);
        }

        internal int ReplicatorCount => _replicators.Count;

        /// <summary>
        /// Re-subscribes <see cref="OnTick"/> to the tail of the multicast delegate.
        /// Called by <see cref="PredictionManager{TInput}"/> when it is created after this
        /// system already exists, so the prediction Simulate pass always runs before the
        /// replication ServerTick in the same frame. Without this, server-side Simulate
        /// writes would mark bindings dirty just after ServerTick drained them, adding a
        /// one-tick relay delay.
        /// </summary>
        internal void RequeueTick()
        {
            if (_disposed) return;
            _networkManager.NetworkTickSystem.Tick -= OnTick;
            _networkManager.NetworkTickSystem.Tick += OnTick;
        }

        internal void Register(EntityReplicator replicator)
        {
            var id = replicator.NetworkObjectId;
            if (_byNetworkObjectId.ContainsKey(id)) return;

            _byNetworkObjectId[id] = replicator;
            var entityIdValue = replicator.EntityId.Value;
            if (entityIdValue != 0)
                _byEntityId[entityIdValue] = replicator;
            _replicators.Add(replicator);
            _snapshotDirty = true;
        }

        internal void Unregister(EntityReplicator replicator)
        {
            var id = replicator.NetworkObjectId;
            if (!_byNetworkObjectId.Remove(id)) return;

            var entityIdValue = replicator.EntityId.Value;
            if (entityIdValue != 0)
                _byEntityId.Remove(entityIdValue);
            _replicators.Remove(replicator);
            _snapshotDirty = true;

            if (_replicators.Count == 0)
                Dispose();
        }

        // Accessors for EntityRefCodec. Explicit interface implementation keeps the
        // resolver surface invisible on the concrete class — the codec depends on the
        // interface, not the system directly. Spawn status is checked here so the codec
        // treats "registered but despawning" the same as "unknown" — callers get a
        // single boolean to branch on.
        bool IEntityRefResolver.TryResolveToNetworkObjectId(EntityId id, out ulong networkObjectId)
        {
            if (id.Value != 0
                && _byEntityId.TryGetValue(id.Value, out var rep)
                && rep.IsSpawned)
            {
                networkObjectId = rep.NetworkObjectId;
                return true;
            }
            networkObjectId = 0;
            return false;
        }

        bool IEntityRefResolver.TryResolveToEntityId(ulong networkObjectId, out EntityId id)
        {
            if (_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) && rep.IsSpawned)
            {
                id = rep.EntityId;
                return true;
            }
            id = EntityId.None;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _networkManager.NetworkTickSystem.Tick -= OnTick;
            _networkManager.OnClientConnectedCallback -= OnClientConnected;
            _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;

            if (_networkManager.CustomMessagingManager != null)
            {
                var messaging = _networkManager.CustomMessagingManager;
                messaging.UnregisterNamedMessageHandler(StateBatchChannel);
                messaging.UnregisterNamedMessageHandler(OwnerSubmitChannel);
                messaging.UnregisterNamedMessageHandler(EventBroadcastChannel);
                messaging.UnregisterNamedMessageHandler(EventBroadcastUnreliableChannel);
                messaging.UnregisterNamedMessageHandler(OwnerEventChannel);
                messaging.UnregisterNamedMessageHandler(OwnerEventUnreliableChannel);
                messaging.UnregisterNamedMessageHandler(SyncRequestChannel);
                messaging.UnregisterNamedMessageHandler(SyncReplyChannel);
            }

            _byNetworkObjectId.Clear();
            _byEntityId.Clear();
            _replicators.Clear();
            _iterationSnapshot = Array.Empty<EntityReplicator>();
            s_Systems.Remove(_networkManager);
        }

        // ------------------------------------------------------------------
        // Tick
        // ------------------------------------------------------------------

        private void OnTick()
        {
            if (_disposed) return;

            if (_snapshotDirty)
            {
                _iterationSnapshot = _replicators.ToArray();
                _snapshotDirty = false;
            }

            if (_broadcastTargetsDirty)
                RebuildBroadcastTargets();

            if (_networkManager.IsServer)
                ServerTick();

            OwnerTick();
        }

        // ACS_StateBatch wire format:
        //   ushort entityCount
        //   [entityCount × per-entity record:
        //       ulong  networkObjectId
        //       ushort payloadBytes     — byte length of (serverTick + mask + fields) that follows
        //       int    serverTick
        //       byte[maskByteCount] mask
        //       bytes  per-dirty-field payload (in binding-index order)
        //   ]
        //
        // The payloadBytes prefix lets OnStateBatchReceived Seek past records whose
        // networkObjectId it does not recognize (late spawn / early despawn race) and
        // continue parsing the batch tail. Assumes field serializers are fixed-size;
        // if variable-length fields are ever added (string, byte[]), audit this width.
        private unsafe void ServerTick()
        {
            _dirtyReplicatorsBuffer.Clear();

            // First pass: compute total payload size, fill dirty masks, and record
            // the dirty replicators so pass 2 can iterate them without rescanning masks.
            int totalPayloadSize = sizeof(ushort); // entityCount header

            for (int e = 0; e < _iterationSnapshot.Length; e++)
            {
                var rep = _iterationSnapshot[e];
                if (!rep.IsSpawned) continue;

                var bindings = rep.Bindings;
                var maskBuffer = rep.DirtyMaskBuffer;
                int maskByteCount = rep.MaskByteCount;

                Array.Clear(maskBuffer, 0, maskByteCount);
                bool anyDirty = false;
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (bindings[i].IsDirty)
                    {
                        maskBuffer[i >> 3] |= (byte)(1 << (i & 7));
                        anyDirty = true;
                    }
                }

                if (!anyDirty) continue;

                _dirtyReplicatorsBuffer.Add(rep);
                // networkObjectId + payloadBytes prefix + serverTick + mask
                totalPayloadSize += sizeof(ulong) + sizeof(ushort) + sizeof(int) + maskByteCount;
                for (int i = 0; i < bindings.Length; i++)
                {
                    if ((maskBuffer[i >> 3] & (1 << (i & 7))) != 0)
                        totalPayloadSize += bindings[i].Size;
                }
            }

            int dirtyCount = _dirtyReplicatorsBuffer.Count;
            if (dirtyCount == 0) return;
            if (_broadcastTargetIds.Count == 0) return;

            int serverTick = _networkManager.NetworkTickSystem.ServerTime.Tick;

            var writer = new FastBufferWriter(totalPayloadSize, Allocator.Temp);
            try
            {
                writer.WriteValueSafe((ushort)dirtyCount);

                // Second pass: iterate only the replicators that pass 1 flagged as dirty.
                for (int k = 0; k < _dirtyReplicatorsBuffer.Count; k++)
                {
                    var rep = _dirtyReplicatorsBuffer[k];
                    var bindings = rep.Bindings;
                    var maskBuffer = rep.DirtyMaskBuffer;
                    int maskByteCount = rep.MaskByteCount;

                    writer.WriteValueSafe(rep.NetworkObjectId);

                    // Reserve the payloadBytes slot; patch after the body is written.
                    int lenPos = writer.Position;
                    writer.Seek(lenPos + sizeof(ushort));
                    int bodyStart = writer.Position;

                    writer.WriteValueSafe(serverTick);
                    fixed (byte* maskPtr = maskBuffer)
                        writer.WriteBytesSafe(maskPtr, maskByteCount);

                    for (int i = 0; i < bindings.Length; i++)
                    {
                        if ((maskBuffer[i >> 3] & (1 << (i & 7))) != 0)
                        {
                            bindings[i].WriteTo(writer);
                            bindings[i].ClearDirty();
                        }
                    }

                    int payloadBytes = writer.Position - bodyStart;
                    Debug.Assert(payloadBytes <= ushort.MaxValue,
                        $"[EntityReplicationSystem] Per-entity payload {payloadBytes} exceeds ushort prefix width.");
                    int endPos = writer.Position;
                    writer.Seek(lenPos);
                    writer.WriteValueSafe((ushort)payloadBytes);
                    writer.Seek(endPos);

                    // Debug probe for bandwidth measurement / experiments. Null unless a
                    // subscriber is attached, so the hot path stays free in production.
                    ReplicationDebug.OnEntityPayloadWritten?.Invoke(rep.NetworkObjectId, payloadBytes);
                }

                _networkManager.CustomMessagingManager.SendNamedMessage(
                    StateBatchChannel, _broadcastTargetIds, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally
            {
                writer.Dispose();
            }
        }

        private unsafe void OwnerTick()
        {
            for (int e = 0; e < _iterationSnapshot.Length; e++)
            {
                var rep = _iterationSnapshot[e];
                if (!rep.IsSpawned) continue;
                if (!rep.IsOwner || rep.IsServer) continue;

                var bindings = rep.Bindings;
                var authorities = rep.BindingAuthorities;
                var maskBuffer = rep.DirtyMaskBuffer;
                int maskByteCount = rep.MaskByteCount;

                Array.Clear(maskBuffer, 0, maskByteCount);
                bool anyDirty = false;
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (authorities[i] != AuthorityMode.Owner) continue;
                    if (bindings[i].IsDirty)
                    {
                        maskBuffer[i >> 3] |= (byte)(1 << (i & 7));
                        anyDirty = true;
                    }
                }

                if (!anyDirty) continue;

                int payloadSize = sizeof(ulong) + sizeof(int) + maskByteCount;
                for (int i = 0; i < bindings.Length; i++)
                {
                    if ((maskBuffer[i >> 3] & (1 << (i & 7))) != 0)
                        payloadSize += bindings[i].Size;
                }

                int clientTick = _networkManager.NetworkTickSystem.ServerTime.Tick;
                var writer = new FastBufferWriter(payloadSize, Allocator.Temp);
                try
                {
                    writer.WriteValueSafe(rep.NetworkObjectId);
                    writer.WriteValueSafe(clientTick);
                    fixed (byte* maskPtr = maskBuffer)
                        writer.WriteBytesSafe(maskPtr, maskByteCount);

                    for (int i = 0; i < bindings.Length; i++)
                    {
                        if ((maskBuffer[i >> 3] & (1 << (i & 7))) != 0)
                        {
                            bindings[i].WriteTo(writer);
                            bindings[i].ClearDirty();
                        }
                    }

                    _networkManager.CustomMessagingManager.SendNamedMessage(
                        OwnerSubmitChannel, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        // ------------------------------------------------------------------
        // Named message handlers
        // ------------------------------------------------------------------

        private void OnStateBatchReceived(ulong senderClientId, FastBufferReader reader)
            => ApplyStateBatch(reader, _resolveSpawned);

        /// <summary>
        /// Parses one <c>ACS_StateBatch</c> payload. Extracted for unit testing without
        /// spinning up a full <see cref="NetworkManager"/>: tests supply their own
        /// <paramref name="resolve"/> over a dictionary of synthetic replicators.
        ///
        /// Per-entity records carry a <c>ushort payloadBytes</c> prefix so records whose
        /// <c>networkObjectId</c> does not resolve can be skipped and the batch tail
        /// still parsed. See <see cref="ServerTick"/> for the matching wire-format
        /// comment.
        /// </summary>
        internal static void ApplyStateBatch(FastBufferReader reader, Func<ulong, EntityReplicator?> resolve)
        {
            reader.ReadValueSafe(out ushort entityCount);

            for (int e = 0; e < entityCount; e++)
            {
                reader.ReadValueSafe(out ulong networkObjectId);
                reader.ReadValueSafe(out ushort payloadBytes);
                int recordStart = reader.Position;
                int nextEntityPos = recordStart + payloadBytes;

                var rep = resolve(networkObjectId);
                if (rep == null)
                {
                    // Unknown or despawned entity — skip the record and continue so the
                    // batch tail is not lost. Late spawn on sender / early despawn on
                    // receiver / spawn-order races all land here.
                    Debug.LogWarning(
                        $"[EntityReplicationSystem] Unknown/despawned entity {networkObjectId} " +
                        $"— skipping {payloadBytes} bytes, continuing batch.");
                    reader.Seek(nextEntityPos);
                    continue;
                }

                var mode = rep.IsOwner ? StateApplyMode.SkipOwnerAuth : StateApplyMode.ApplyAll;
                rep.ApplyStateBuffer(reader, mode, out int serverTick);
                // Step 7: hand the just-applied server tick to the prediction pipeline
                // so locally-owning entities can rewind+replay against authoritative
                // state. No-op on entities without Predicted fields.
                rep.NotifyServerStateApplied(serverTick);

                // Defensive realign: if ApplyStateBuffer under-reads (upstream bug), log
                // and snap forward so the rest of the batch is still parseable. This
                // should never trigger in practice — the payload was produced by the
                // symmetric ServerTick writer — but silent desync here would hide
                // exactly the class of bug this batch change is designed to prevent.
                if (reader.Position != nextEntityPos)
                {
                    Debug.LogError(
                        $"[EntityReplicationSystem] ApplyStateBuffer read {reader.Position - recordStart} bytes, " +
                        $"expected {payloadBytes} for entity {networkObjectId}. Realigning.");
                    reader.Seek(nextEntityPos);
                }
            }
        }

        private unsafe void OnOwnerSubmitReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);
            reader.ReadValueSafe(out int senderTick);

            if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) || !rep.IsSpawned)
            {
                Debug.LogWarning($"[EntityReplicationSystem] Received owner submit for unknown/despawned entity {networkObjectId}.");
                return;
            }

            rep.ApplyOwnerSubmission(reader, senderTick);
        }

        private void OnEventBroadcastReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);

            if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) || !rep.IsSpawned)
                return;

            reader.ReadValueSafe(out byte eventIndex);
            rep.DispatchEvent(eventIndex, reader);
        }

        private void OnOwnerEventReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);

            if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) || !rep.IsSpawned)
                return;

            reader.ReadValueSafe(out byte eventIndex);
            rep.HandleOwnerEvent(eventIndex, reader, this);
        }

        private void OnSyncRequestReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);

            if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) || !rep.IsSpawned)
                return;

            var writer = new FastBufferWriter(sizeof(ulong) + rep.InitialSyncPayloadHint, Allocator.Temp);
            try
            {
                writer.WriteValueSafe(networkObjectId);
                rep.BuildInitialSyncPayload(writer);
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    SyncReplyChannel, senderClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally
            {
                writer.Dispose();
            }
        }

        private void OnSyncReplyReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);

            if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) || !rep.IsSpawned)
                return;

            rep.ApplyStateBuffer(reader, StateApplyMode.SkipOwnerAuthIfLocallyWritten, out _);
        }

        // ------------------------------------------------------------------
        // IEventBroadcaster
        // ------------------------------------------------------------------

        void IEventBroadcaster.SendEvent(ulong networkObjectId, byte eventIndex,
            FastBufferWriter writer, AuthorityMode authority, Reliability reliability, bool isOwnerSubmit)
        {
            string channel;
            ulong targetClientId;

            if (isOwnerSubmit)
            {
                channel = reliability == Reliability.Reliable ? OwnerEventChannel : OwnerEventUnreliableChannel;
                targetClientId = NetworkManager.ServerClientId;

                _networkManager.CustomMessagingManager.SendNamedMessage(
                    channel, targetClientId, writer,
                    reliability == Reliability.Reliable ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.Unreliable);
            }
            else
            {
                channel = reliability == Reliability.Reliable ? EventBroadcastChannel : EventBroadcastUnreliableChannel;

                if (_broadcastTargetIds.Count == 0) return;
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    channel, _broadcastTargetIds, writer,
                    reliability == Reliability.Reliable ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.Unreliable);
            }
        }

        // ------------------------------------------------------------------
        // Initial sync
        // ------------------------------------------------------------------

        internal void RequestInitialSync(EntityReplicator replicator)
        {
            var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp);
            try
            {
                writer.WriteValueSafe(replicator.NetworkObjectId);
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    SyncRequestChannel, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally
            {
                writer.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // Broadcast target management
        // ------------------------------------------------------------------

        private void OnClientConnected(ulong clientId) => _broadcastTargetsDirty = true;
        private void OnClientDisconnected(ulong clientId) => _broadcastTargetsDirty = true;

        private void RebuildBroadcastTargets()
        {
            _broadcastTargetsDirty = false;
            _broadcastTargetIds.Clear();

            foreach (var clientId in _networkManager.ConnectedClientsIds)
            {
                // Exclude the local client on the host — it already has the
                // latest values written directly by server-side logic.
                if (clientId == _networkManager.LocalClientId && _networkManager.IsServer)
                    continue;
                _broadcastTargetIds.Add(clientId);
            }
        }
    }
}
