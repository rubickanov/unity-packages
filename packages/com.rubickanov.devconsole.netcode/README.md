# DevConsole Netcode

Netcode extension for [DevConsole](../com.rubickanov.devconsole/). Adds CS:GO-style command domains (Client/Server/Shared) and `sv_cheats`-gated cheat protection for Netcode for GameObjects.

## Dependencies

- `com.rubickanov.devconsole` — base console, command registry, attributes
- `com.unity.netcode.gameobjects` — RPCs, `NetworkVariable`, `NetworkBehaviour`

## Quick Start

Add **NetworkCommandBridge** to a `NetworkObject` that lives for the whole session (e.g. your network manager). On `OnNetworkSpawn` it scans every registered command for domain/cheat attributes, registers the built-in `sv_cheats` command, and installs a pre-execute filter on the shared `CommandRegistry`. Without the bridge spawned, the console runs in plain local mode.

```csharp
// No code needed beyond attaching the component — it wires itself in OnNetworkSpawn.
public class NetworkBootstrap : NetworkBehaviour
{
    [SerializeField] NetworkCommandBridge bridge;
}
```

On `OnNetworkDespawn` the filter and `sv_cheats` are removed, reverting the console to local-only.

## Usage

### Command Domains

Commands are still declared with the base package's `[ConsoleCommand]`. Add `[CommandDomain]` to control where they run. Commands without it default to `Shared`.

```csharp
using Rubickanov.DevConsole;
using Rubickanov.DevConsole.Netcode;

[ConsoleCommand("kick", "Kick a client by ID", "Network")]
[CommandDomain(CommandDomain.Server)]
public static void Kick(ulong clientId) { /* runs on the server */ }

[ConsoleCommand("ping", "Show round-trip time", "Network")]
[CommandDomain(CommandDomain.Client)]
public static void Ping() { /* runs locally on the client */ }
```

| Domain | Behavior |
|--------|----------|
| `Shared` | Executes locally on whoever typed it (default) |
| `Client` | Executes locally only, never sent to the server |
| `Server` | Host runs it locally; clients forward it to the server via RPC |

When a client types a `Server`-domain command, the bridge sends the raw input through `ExecuteOnServerRpc`. The server re-runs it through the same filter (so domains and cheat checks are re-enforced server-side, never trusting the client) and ships the result back to the requesting client only.

### Cheat Protection

Mark commands with `[CheatProtected]` so they only run while cheats are enabled.

```csharp
[ConsoleCommand("god", "Toggle invulnerability", "Cheats")]
[CommandDomain(CommandDomain.Server)]
[CheatProtected]
public static void God() { /* requires sv_cheats 1 */ }
```

Cheats are a server-authoritative flag exposed as `NetworkCommandBridge.CheatsEnabled` (a `NetworkVariable<bool>`). Toggle it with the built-in `sv_cheats` command, which is itself `Server`-domain — a client typing it gets routed to the server:

```text
sv_cheats 1    # server sets CheatsEnabled = true
sv_cheats      # prints the current value
```

The filter blocks any `[CheatProtected]` command with an error while `CheatsEnabled.Value` is false.

### Identifying the Caller

During server-side RPC execution, `NetworkCommandBridge.ExecutingClientId` (a `static ulong?`) holds the sender's client ID. It is `null` for local/host execution, so a command can tell who invoked it.

```csharp
[ConsoleCommand("whoami", "Print the calling client ID", "Network")]
[CommandDomain(CommandDomain.Server)]
public static void WhoAmI()
{
    var caller = NetworkCommandBridge.ExecutingClientId;
    ConsoleLog.Log(caller.HasValue ? $"Client {caller.Value}" : "Host (local)");
}
```

### Built-in Commands

The package ships a set of network commands, registered automatically with the base console: `status`, `players`, `ping`, `net_stats`, `kick`, `disconnect`, plus `sv_cheats` from the bridge.
