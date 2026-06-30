# Steam Networking Sockets Transport

Netcode for GameObjects transport built on Steam Networking Sockets P2P relay. Runs as client, host, or dedicated server (`UNITY_SERVER`).

## Dependencies

- `com.unity.netcode.gameobjects` — `NetworkTransport` base type this package implements
- `com.rlabrecque.steamworks.net` — Steamworks.NET bindings for the Steam Networking Sockets API

Requires Steam to be initialized before the transport starts (see Quick Start).

## Architecture

```
SteamNetworkingSocketsTransport : NetworkTransport
    ├── StartServer()   — InitRelayNetworkAccess + CreateListenSocketP2P + poll group
    ├── StartClient()   — InitRelayNetworkAccess + ConnectP2P to ConnectToSteamID
    ├── Send()          — SendMessageToConnection (delivery → Steam send flags)
    ├── PollEvent()     — drains connect/disconnect events, then received messages
    └── Shutdown()      — close all connections + listen socket + poll group
```

The transport keeps a bidirectional map between NGO `ulong` client IDs and Steam `HSteamNetConnection` handles. Connection lifecycle is driven by a `SteamNetConnectionStatusChangedCallback_t` callback: incoming connections are accepted and assigned to the poll group on the server; connect/disconnect transitions are queued as `NetworkEvent`s that `PollEvent` surfaces to NGO.

Every Steam call is compiled against either `SteamNetworkingSockets` or `SteamGameServerNetworkingSockets` depending on the `UNITY_SERVER` scripting define, so the same transport class serves both player-hosted and dedicated-server builds.

## Core Concepts

**ConnectToSteamID** — A `CSteamID` the client connects to. Set it to the host's Steam ID before calling `StartClient`.

**ServerClientId** — Always `0`. On a client, the host connection is registered under this ID; on the server it identifies the local host.

## Quick Start

1. Initialize Steam (`SteamAPI.Init()`, or `GameServer.Init()` for dedicated servers) before touching the network.
2. Add the **SteamNetworkingSocketsTransport** component to your `NetworkManager` GameObject.
3. Assign it as the active transport.

```csharp
var transport = networkManager.GetComponent<SteamNetworkingSocketsTransport>();
networkManager.NetworkConfig.NetworkTransport = transport;
```

## Usage

### Hosting

```csharp
networkManager.StartHost();
// Share SteamUser.GetSteamID() so clients can connect to this lobby.
```

### Joining

`ConnectToSteamID` is a `CSteamID`, not a raw `ulong` — wrap a 64-bit Steam ID before assigning.

```csharp
transport.ConnectToSteamID = new CSteamID(hostSteamId64);
networkManager.StartClient();
```

### Dedicated Server

Build with the `UNITY_SERVER` scripting define. Every socket operation then routes through `SteamGameServerNetworkingSockets`, so initialize Steam via `GameServer.Init()` rather than `SteamAPI.Init()`.

```csharp
networkManager.StartServer();
```

### Delivery Modes

NGO delivery modes map to Steam send flags as follows:

| NetworkDelivery | Steam Flag |
|-----------------|------------|
| `Unreliable` | `k_nSteamNetworkingSend_Unreliable` |
| `UnreliableSequenced` | `k_nSteamNetworkingSend_UnreliableNoNagle` |
| `Reliable` | `k_nSteamNetworkingSend_Reliable` |
| `ReliableSequenced` | `k_nSteamNetworkingSend_ReliableNoNagle` |
| `ReliableFragmentedSequenced` | `k_nSteamNetworkingSend_Reliable` |

Anything unrecognized falls back to `k_nSteamNetworkingSend_Reliable`.

`UnreliableSequenced` is not truly sequenced. Steam has no unreliable-sequenced primitive; `NoNagle` only disables Nagle batching and does not drop stale or out-of-order packets. A late packet can be delivered after a newer one, so NGO's "a newer packet supersedes an older one" contract is not honored on this mode.

### Round-Trip Time

```csharp
ulong rttMs = transport.GetCurrentRtt(clientId); // 0 if the client is unknown or status is unavailable
```

### Disconnecting

```csharp
transport.DisconnectLocalClient();          // client leaves the host
transport.DisconnectRemoteClient(clientId); // host kicks a specific client
```

## Design Decisions

- **Single class, both Steam backends** — `UNITY_SERVER` switches between `SteamNetworkingSockets` and `SteamGameServerNetworkingSockets` at compile time instead of duplicating the transport, so client and dedicated-server builds share one code path.
- **InitRelayNetworkAccess on both start paths** — Steam fetches relay config asynchronously; kicking it off before opening the listen socket (server) or connecting (client) keeps the first P2P connections from stalling while the relay network comes online.
- **Messages buffered in PollEvent** — Steam returns a batch of messages per receive call, but NGO's `PollEvent` yields one event at a time, so the batch is copied into a pending queue and drained across successive polls.
