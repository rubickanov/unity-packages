using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.Netcode.Transports
{
    public class SteamNetworkingSocketsTransport : NetworkTransport
    {
        public CSteamID ConnectToSteamID { get; set; }

        private HSteamListenSocket _listenSocket;
        private HSteamNetPollGroup _pollGroup;

        private readonly Dictionary<ulong, HSteamNetConnection> _clientToConnection = new();
        private readonly Dictionary<HSteamNetConnection, ulong> _connectionToClient = new();

        private ulong _nextClientId = 1;
        private ulong _serverClientId;
        private bool _isServer;

        private Callback<SteamNetConnectionStatusChangedCallback_t>? _connectionStatusChanged;

        private readonly Queue<PendingEvent> _pendingEvents = new();
        private readonly Queue<PendingMessage> _pendingMessages = new();
        private readonly IntPtr[] _messagePointers = new IntPtr[64];

        public override ulong ServerClientId => _serverClientId;

        // ── Server ──────────────────────────────────────────────────

        public override bool StartServer()
        {
            _isServer = true;
            _connectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            // Steam fetches relay network config asynchronously; kick it off before opening the
            // listen socket (as StartClient does) so the first incoming P2P connections don't
            // stall waiting for the relay network to come online.
#if UNITY_SERVER
            SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
            _listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            _pollGroup = SteamGameServerNetworkingSockets.CreatePollGroup();
#else
            SteamNetworkingUtils.InitRelayNetworkAccess();
            _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            _pollGroup = SteamNetworkingSockets.CreatePollGroup();
#endif

            return true;
        }

        // ── Client ──────────────────────────────────────────────────

        public override bool StartClient()
        {
            _isServer = false;
            _connectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

#if UNITY_SERVER
            SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
#else
            SteamNetworkingUtils.InitRelayNetworkAccess();
#endif

            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(ConnectToSteamID);

#if UNITY_SERVER
            var connection = SteamGameServerNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
#else
            var connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
#endif

            _serverClientId = 0;
            _clientToConnection[_serverClientId] = connection;
            _connectionToClient[connection] = _serverClientId;

            return true;
        }

        // ── Send ────────────────────────────────────────────────────

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            if (!_clientToConnection.TryGetValue(clientId, out var connection))
                return;

            int sendFlags = networkDelivery switch
            {
                NetworkDelivery.Unreliable => Constants.k_nSteamNetworkingSend_Unreliable,
                // CAVEAT: Steam has no unreliable-sequenced primitive. NoNagle only disables Nagle
                // batching — it does NOT drop stale/out-of-order packets, so NGO's "sequenced"
                // contract (a newer packet supersedes an older one) is not honored here and a late
                // packet can be applied after a newer one. Documented in the README delivery table.
                NetworkDelivery.UnreliableSequenced => Constants.k_nSteamNetworkingSend_UnreliableNoNagle,
                NetworkDelivery.Reliable => Constants.k_nSteamNetworkingSend_Reliable,
                NetworkDelivery.ReliableSequenced => Constants.k_nSteamNetworkingSend_ReliableNoNagle,
                NetworkDelivery.ReliableFragmentedSequenced => Constants.k_nSteamNetworkingSend_Reliable,
                _ => Constants.k_nSteamNetworkingSend_Reliable,
            };

            var handle = GCHandle.Alloc(payload.Array!, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject() + payload.Offset;
#if UNITY_SERVER
                var result = SteamGameServerNetworkingSockets.SendMessageToConnection(
                    connection, ptr, (uint)payload.Count, sendFlags, out _);
#else
                var result = SteamNetworkingSockets.SendMessageToConnection(
                    connection, ptr, (uint)payload.Count, sendFlags, out _);
#endif
                if (result != EResult.k_EResultOK)
                    Debug.LogWarning($"[SteamTransport] Send failed: {result}");
            }
            finally
            {
                handle.Free();
            }
        }

        // ── Poll ────────────────────────────────────────────────────

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            payload = default;
            receiveTime = Time.realtimeSinceStartup;

            // 1. Drain pending connect/disconnect events first
            if (_pendingEvents.TryDequeue(out var pending))
            {
                clientId = pending.ClientId;

                if (pending.Type == NetworkEvent.Disconnect && pending.Connection != default)
                {
                    _connectionToClient.Remove(pending.Connection);
                    _clientToConnection.Remove(pending.ClientId);
                }

                return pending.Type;
            }

            // 2. Drain buffered messages
            if (_pendingMessages.TryDequeue(out var buffered))
            {
                clientId = buffered.ClientId;
                payload = buffered.Payload;
                return NetworkEvent.Data;
            }

            // 3. Receive new messages
            int messageCount;
            if (_isServer)
            {
#if UNITY_SERVER
                messageCount = SteamGameServerNetworkingSockets.ReceiveMessagesOnPollGroup(
                    _pollGroup, _messagePointers, _messagePointers.Length);
#else
                messageCount = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(
                    _pollGroup, _messagePointers, _messagePointers.Length);
#endif
            }
            else
            {
                if (!_clientToConnection.TryGetValue(_serverClientId, out var connection))
                    return NetworkEvent.Nothing;

#if UNITY_SERVER
                messageCount = SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(
                    connection, _messagePointers, _messagePointers.Length);
#else
                messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(
                    connection, _messagePointers, _messagePointers.Length);
#endif
            }

            if (messageCount <= 0)
                return NetworkEvent.Nothing;

            // Process all received messages
            for (int i = 0; i < messageCount; i++)
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(_messagePointers[i]);

                var data = new byte[message.m_cbSize]; // allocation but we ignored it for simplicity
                Marshal.Copy(message.m_pData, data, 0, message.m_cbSize);

                SteamNetworkingMessage_t.Release(_messagePointers[i]);

                ulong msgClientId;
                if (_isServer)
                {
                    if (!_connectionToClient.TryGetValue(message.m_conn, out msgClientId))
                    {
                        Debug.LogWarning($"[SteamTransport] Received message from unknown connection {message.m_conn}, dropping.");
                        continue;
                    }
                }
                else
                {
                    msgClientId = _serverClientId;
                }

                _pendingMessages.Enqueue(new PendingMessage(msgClientId, new ArraySegment<byte>(data)));
            }

            // Return first buffered message
            if (_pendingMessages.TryDequeue(out var first))
            {
                clientId = first.ClientId;
                payload = first.Payload;
                return NetworkEvent.Data;
            }

            return NetworkEvent.Nothing;
        }

        // ── Connection Status ───────────────────────────────────────

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t info)
        {
            var state = info.m_info.m_eState;
            var oldState = info.m_eOldState;

            if (_isServer)
            {
                HandleServerConnectionStatus(info, state, oldState);
            }
            else
            {
                HandleClientConnectionStatus(info, state, oldState);
            }
        }

        private void HandleServerConnectionStatus(
            SteamNetConnectionStatusChangedCallback_t info,
            ESteamNetworkingConnectionState state,
            ESteamNetworkingConnectionState oldState)
        {
            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
#if UNITY_SERVER
                var result = SteamGameServerNetworkingSockets.AcceptConnection(info.m_hConn);
                if (result == EResult.k_EResultOK)
                    SteamGameServerNetworkingSockets.SetConnectionPollGroup(info.m_hConn, _pollGroup);
                else
                    Debug.LogWarning($"[SteamTransport] AcceptConnection failed: {result}");
#else
                var result = SteamNetworkingSockets.AcceptConnection(info.m_hConn);
                if (result == EResult.k_EResultOK)
                    SteamNetworkingSockets.SetConnectionPollGroup(info.m_hConn, _pollGroup);
                else
                    Debug.LogWarning($"[SteamTransport] AcceptConnection failed: {result}");
#endif
            }
            else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                ulong cid = _nextClientId++;
                _clientToConnection[cid] = info.m_hConn;
                _connectionToClient[info.m_hConn] = cid;

                _pendingEvents.Enqueue(new PendingEvent(NetworkEvent.Connect, cid));
            }
            else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                     || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (_connectionToClient.TryGetValue(info.m_hConn, out ulong cid))
                {
                    _pendingEvents.Enqueue(new PendingEvent(NetworkEvent.Disconnect, cid, info.m_hConn));
                }

#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
#else
                SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
#endif
            }
        }

        private void HandleClientConnectionStatus(
            SteamNetConnectionStatusChangedCallback_t info,
            ESteamNetworkingConnectionState state,
            ESteamNetworkingConnectionState oldState)
        {
            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                _pendingEvents.Enqueue(new PendingEvent(NetworkEvent.Connect, _serverClientId));
            }
            else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                     || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                _pendingEvents.Enqueue(new PendingEvent(NetworkEvent.Disconnect, _serverClientId, info.m_hConn));

#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
#else
                SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
#endif
            }
        }

        // ── RTT ─────────────────────────────────────────────────────

        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (!_clientToConnection.TryGetValue(clientId, out var connection))
                return 0;

            var status = new SteamNetConnectionRealTimeStatus_t();
            var laneStatus = new SteamNetConnectionRealTimeLaneStatus_t();
#if UNITY_SERVER
            if (SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref laneStatus) == EResult.k_EResultOK)
#else
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref laneStatus) == EResult.k_EResultOK)
#endif
            {
                return (ulong)Mathf.Max(0, status.m_nPing);
            }

            return 0;
        }

        // ── Disconnect ──────────────────────────────────────────────

        public override void DisconnectLocalClient()
        {
            if (_clientToConnection.TryGetValue(_serverClientId, out var connection))
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(connection, 0, null, false);
#else
                SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
#endif
                _connectionToClient.Remove(connection);
                _clientToConnection.Remove(_serverClientId);
            }
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (_clientToConnection.TryGetValue(clientId, out var connection))
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(connection, 0, null, false);
#else
                SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
#endif
                _connectionToClient.Remove(connection);
                _clientToConnection.Remove(clientId);
            }
        }

        // ── Shutdown ────────────────────────────────────────────────

        public override void Shutdown()
        {
            _connectionStatusChanged?.Dispose();
            _connectionStatusChanged = null;

            // Close all connections
            foreach (var connection in _connectionToClient.Keys)
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(connection, 0, null, false);
#else
                SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
#endif
            }

            _clientToConnection.Clear();
            _connectionToClient.Clear();
            _pendingEvents.Clear();
            _pendingMessages.Clear();

            // Close listen socket and poll group
            if (_listenSocket != default)
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseListenSocket(_listenSocket);
#else
                SteamNetworkingSockets.CloseListenSocket(_listenSocket);
#endif
                _listenSocket = default;
            }

            if (_pollGroup != default)
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.DestroyPollGroup(_pollGroup);
#else
                SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
#endif
                _pollGroup = default;
            }

            _nextClientId = 1;
            _isServer = false;
        }

        // ── Init ────────────────────────────────────────────────────

        public override void Initialize(NetworkManager? networkManager = null)
        {
            // No special initialization needed; Steam must already be initialized.
        }

        // ── Helpers ──────────────────────────────────────────────────

        private readonly struct PendingEvent
        {
            public readonly NetworkEvent Type;
            public readonly ulong ClientId;
            public readonly HSteamNetConnection Connection;

            public PendingEvent(NetworkEvent type, ulong clientId, HSteamNetConnection connection = default)
            {
                Type = type;
                ClientId = clientId;
                Connection = connection;
            }
        }

        private readonly struct PendingMessage
        {
            public readonly ulong ClientId;
            public readonly ArraySegment<byte> Payload;

            public PendingMessage(ulong clientId, ArraySegment<byte> payload)
            {
                ClientId = clientId;
                Payload = payload;
            }
        }
    }
}
