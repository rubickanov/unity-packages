# Steam Networking Sockets Transport

Netcode for GameObjects transport using Steam Networking Sockets P2P.

## Requirements

- Unity 2022.3+
- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) (`com.rlabrecque.steamworks.net`)
- [Netcode for GameObjects](https://docs-multiplayer.unity3d.com/) (`com.unity.netcode.gameobjects`)

## Usage

1. Add `SteamNetworkingSocketsTransport` component to your NetworkManager GameObject.
2. Set it as the active transport on the NetworkManager.
3. Ensure Steam is initialized before starting the network.

### Host

```csharp
networkManager.NetworkConfig.NetworkTransport = steamTransport;
networkManager.StartHost();
```

### Client

```csharp
steamTransport.ConnectToSteamID = hostSteamId;
networkManager.NetworkConfig.NetworkTransport = steamTransport;
networkManager.StartClient();
```

## Dedicated Server

Uses `SteamGameServerNetworkingSockets` when built with `UNITY_SERVER` define.
