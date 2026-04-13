using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Centralized replication system that collects dirty state from all registered
    /// AspectReplicators and sends one batched message per tick via CustomMessagingManager.
    /// Eliminates per-entity tick subscriptions (#10), per-tick byte[] allocations (#6),
    /// per-event byte[] allocations (#7), managed→native copies (#8), and per-entity
    /// broadcaster delegates (#11).
    /// </summary>
    internal sealed class AspectReplicationSystem : IDisposable, IEventBroadcaster
    {
        private const string StateBatchChannel = "ACS_StateBatch";
        private const string OwnerSubmitChannel = "ACS_OwnerSubmit";
        private const string EventBroadcastChannel = "ACS_EventBcast";
        private const string EventBroadcastUnreliableChannel = "ACS_EventBcastU";
        private const string OwnerEventChannel = "ACS_OwnerEvt";
        private const string OwnerEventUnreliableChannel = "ACS_OwnerEvtU";
        private const string SyncRequestChannel = "ACS_SyncReq";
        private const string SyncReplyChannel = "ACS_SyncReply";

        private static readonly Dictionary<NetworkManager, AspectReplicationSystem> s_Systems = new();

        private readonly NetworkManager _networkManager;
        private readonly Dictionary<ulong, AspectReplicator> _byNetworkObjectId = new();
        private readonly List<AspectReplicator> _replicators = new();
        private AspectReplicator[] _iterationSnapshot = Array.Empty<AspectReplicator>();
        private bool _snapshotDirty;
        private readonly List<ulong> _broadcastTargetIds = new();
        private bool _broadcastTargetsDirty = true;
        private bool _disposed;

        private AspectReplicationSystem(NetworkManager networkManager)
        {
            _networkManager = networkManager;

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

        internal static AspectReplicationSystem GetOrCreate(NetworkManager networkManager)
        {
            if (!s_Systems.TryGetValue(networkManager, out var system))
            {
                system = new AspectReplicationSystem(networkManager);
                s_Systems[networkManager] = system;
            }
            return system;
        }

        // Exposed for tests that need to verify cleanup.
        internal static bool TryGet(NetworkManager networkManager, out AspectReplicationSystem system)
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

        internal void Register(AspectReplicator replicator)
        {
            var id = replicator.NetworkObjectId;
            if (_byNetworkObjectId.ContainsKey(id)) return;

            _byNetworkObjectId[id] = replicator;
            _replicators.Add(replicator);
            _snapshotDirty = true;
        }

        internal void Unregister(AspectReplicator replicator)
        {
            var id = replicator.NetworkObjectId;
            if (!_byNetworkObjectId.Remove(id)) return;

            _replicators.Remove(replicator);
            _snapshotDirty = true;

            if (_replicators.Count == 0)
                Dispose();
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
            _replicators.Clear();
            _iterationSnapshot = Array.Empty<AspectReplicator>();
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

        private unsafe void ServerTick()
        {
            // First pass: compute total payload size and check if anything is dirty.
            int dirtyCount = 0;
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

                dirtyCount++;
                totalPayloadSize += sizeof(ulong) + sizeof(int) + maskByteCount; // networkObjectId + serverTick + mask
                for (int i = 0; i < bindings.Length; i++)
                {
                    if ((maskBuffer[i >> 3] & (1 << (i & 7))) != 0)
                        totalPayloadSize += bindings[i].Size;
                }
            }

            if (dirtyCount == 0) return;
            if (_broadcastTargetIds.Count == 0) return;

            int serverTick = _networkManager.NetworkTickSystem.ServerTime.Tick;

            var writer = new FastBufferWriter(totalPayloadSize, Allocator.Temp);
            try
            {
                writer.WriteValueSafe((ushort)dirtyCount);

                for (int e = 0; e < _iterationSnapshot.Length; e++)
                {
                    var rep = _iterationSnapshot[e];
                    if (!rep.IsSpawned) continue;

                    var bindings = rep.Bindings;
                    var maskBuffer = rep.DirtyMaskBuffer;
                    int maskByteCount = rep.MaskByteCount;

                    // Check if this entity had any dirty fields (mask was set in first pass).
                    bool anyDirty = false;
                    for (int j = 0; j < maskByteCount; j++)
                    {
                        if (maskBuffer[j] != 0) { anyDirty = true; break; }
                    }
                    if (!anyDirty) continue;

                    writer.WriteValueSafe(rep.NetworkObjectId);
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

        private unsafe void OnStateBatchReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort entityCount);

            for (int e = 0; e < entityCount; e++)
            {
                reader.ReadValueSafe(out ulong networkObjectId);

                if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) || !rep.IsSpawned)
                {
                    // Entity unknown or despawned — skip this entry. We need to know
                    // the mask byte count to skip the correct number of bytes. Since we
                    // don't have a replicator, we can't know the layout. Log and bail.
                    Debug.LogWarning($"[AspectReplicationSystem] Received state for unknown/despawned entity {networkObjectId}. Remaining batch entries lost.");
                    return;
                }

                var mode = rep.IsOwner ? StateApplyMode.SkipOwnerAuth : StateApplyMode.ApplyAll;
                rep.ApplyStateBuffer(reader, mode, out int serverTick);
                // Step 7: hand the just-applied server tick to the prediction pipeline
                // so locally-owning entities can rewind+replay against authoritative
                // state. No-op on entities without Predicted fields.
                rep.NotifyServerStateApplied(serverTick);
            }
        }

        private unsafe void OnOwnerSubmitReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);
            reader.ReadValueSafe(out int senderTick);

            if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var rep) || !rep.IsSpawned)
            {
                Debug.LogWarning($"[AspectReplicationSystem] Received owner submit for unknown/despawned entity {networkObjectId}.");
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

            var writer = new FastBufferWriter(sizeof(ulong) + rep.StatePayloadCap, Allocator.Temp);
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

        internal void RequestInitialSync(AspectReplicator replicator)
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
