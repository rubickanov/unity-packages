# Steam Networking Sockets Transport

Netcode for GameObjects transport using Steam Networking Sockets P2P relay. Supports client, host, and dedicated server (`UNITY_SERVER`) modes.

## Dependencies

- `com.unity.netcode.gameobjects` — Netcode for GameObjects transport layer
- `com.rlabrecque.steamworks.net` — Steamworks.NET bindings

## Architecture

```
SteamNetworkingSocketsTransport : NetworkTransport
    ├── StartServer()   — CreateListenSocketP2P + poll group
    ├── StartClient()   — ConnectP2P to target SteamID
    ├── Send()          — SendMessageToConnection (reliable/unreliable)
    ├── PollEvent()     — ReceiveMessagesOnPollGroup/Connection
    └── Shutdown()      — close all connections + listen socket
```

**SteamNetworkingSocketsTransport** extends Unity's **NetworkTransport**. On server, it creates a P2P listen socket and a poll group. On client, it connects to a target Steam ID. All API calls switch between `SteamNetworkingSockets` and `SteamGameServerNetworkingSockets` based on the `UNITY_SERVER` define.

## Quick Start

1. Ensure Steam is initialized (e.g., `SteamAPI.Init()`) before starting the network.
2. Add **SteamNetworkingSocketsTransport** component to your NetworkManager GameObject.
3. Assign it as the active transport.

```csharp
var transport = networkManager.GetComponent<SteamNetworkingSocketsTransport>();
networkManager.NetworkConfig.NetworkTransport = transport;
```

## Usage

### Host

```csharp
networkManager.StartHost();
```

### Client

```csharp
transport.ConnectToSteamID = hostSteamId;
networkManager.StartClient();
```

### Dedicated Server

Build with the `UNITY_SERVER` scripting define. The transport automatically uses `SteamGameServerNetworkingSockets` for all socket operations.

### Delivery Modes

The transport maps Netcode delivery modes to Steam send flags:

| NetworkDelivery | Steam Flag |
|-----------------|------------|
| Unreliable | `k_nSteamNetworkingSend_Unreliable` |
| UnreliableSequenced | `k_nSteamNetworkingSend_UnreliableNoNagle` |
| Reliable | `k_nSteamNetworkingSend_Reliable` |
| ReliableSequenced | `k_nSteamNetworkingSend_ReliableNoNagle` |

### RTT

```csharp
ulong rtt = transport.GetCurrentRtt(clientId);
```
