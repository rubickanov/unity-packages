# DevConsole Netcode

Netcode extension for [DevConsole](../com.rubickanov.devconsole/). Adds CS:GO-style command domains (Client/Server/Shared) and cheat protection via `sv_cheats`.

## Dependencies

- `com.rubickanov.devconsole` — base DevConsole package
- `com.unity.netcode.gameobjects` — Netcode for GameObjects

## Quick Start

Add **NetworkCommandBridge** to a NetworkObject that exists for the lifetime of the session (e.g., a network manager). Without it, the console works in local/singleplayer mode as usual.

```csharp
// NetworkCommandBridge auto-scans commands for domain/cheat attributes on OnNetworkSpawn.
// No additional setup needed.
```

## Usage

### Command Domains

Annotate command methods to control where they execute:

```csharp
[CommandDomain(CommandDomain.Server)]
public static string Kick(string[] args) { /* runs on server only */ }

[CommandDomain(CommandDomain.Client)]
public static string ToggleHUD(string[] args) { /* runs locally only */ }
```

| Domain | Behavior |
|--------|----------|
| `Shared` | Executes locally on whoever typed it (default) |
| `Client` | Executes only on the local client, never sent to server |
| `Server` | Executes on the server; clients send it via RPC automatically |

Commands without `[CommandDomain]` default to `Shared`.

### Cheat Protection

Mark commands that require cheats to be enabled:

```csharp
[CheatProtected]
[CommandDomain(CommandDomain.Server)]
public static string God(string[] args) { /* requires sv_cheats 1 */ }
```

Toggle cheats via the built-in `sv_cheats` command (server-only):

```
sv_cheats 1
```

**NetworkCommandBridge** registers a pre-execute filter that blocks `[CheatProtected]` commands when `CheatsEnabled` is false and routes `Server`-domain commands from clients to the server via RPC. Results are sent back to the requesting client.

The static property `NetworkCommandBridge.ExecutingClientId` is set during server-side RPC execution, allowing commands to identify which client sent the request.
