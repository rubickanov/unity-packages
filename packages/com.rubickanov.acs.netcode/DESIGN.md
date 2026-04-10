# ACS Netcode - Design Plan

## Overview

Declarative networking layer for ACS built on top of Netcode for GameObjects (NGO).
Goal: add attributes to aspect fields + plug in systems via DI = networking works.
No special base classes for components, no manual NetworkVariable management.
No extra components to add manually -- everything is auto-detected from attributes.

---

## Core Concepts

### Architecture

```
Entity (root GameObject)
|- EntityContext              -- aspects (existing), auto-creates network layer if [Replicated] fields found
|- NetworkObject              -- required by NGO (already needed for any networked entity)
|- MovementComponent          -- regular EntityComponent + ISimulate
|- HealthComponent            -- regular EntityComponent (no ISimulate, no prediction)
```

Components remain regular `EntityComponent`. They don't know about networking.

Under the hood, `EntityContext` detects `[Replicated]` fields on aspects and auto-adds
an internal `AspectReplicator` (NetworkBehaviour) at runtime. The user never touches it.

`PredictionManager` is a singleton pure C# class registered in DI.

### Data Flow

```
Server Authoritative:
  Client: input --> [RPC] --> Server
  Server: Simulate(input) --> state --> [Replicated] --> Client
  Client: Simulate(input) locally (prediction) + reconcile on server state

Client Authoritative:
  Client: Simulate(input) --> state --> [Replicated Owner] --> Server --> Others
  No prediction/reconciliation needed
```

---

## Layer 0: Replication

Two kinds of data flow across the network:

- **State** — `ReactiveProperty<T>` fields. Current value is synchronized via dirty-tick.
  Marked with `[ReplicatedState]`.
- **Events** — `Subject<T>` fields. Each `OnNext` call is broadcast as an instant RPC.
  Marked with `[ReplicatedEvent]`.

### Attributes

```csharp
[AttributeUsage(AttributeTargets.Field)]
public sealed class ReplicatedStateAttribute : Attribute
{
    public AuthorityMode Authority { get; set; } = AuthorityMode.Server;
    public InterpolationMode Interpolation { get; set; } = InterpolationMode.None;
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class ReplicatedEventAttribute : Attribute
{
    public AuthorityMode Authority { get; set; } = AuthorityMode.Server;
    public Reliability Reliability { get; set; } = Reliability.Reliable;
}

public enum AuthorityMode
{
    Server,  // server writes / fires, clients receive
    Owner    // owner writes / fires, server relays to others
}

public enum InterpolationMode
{
    None,
    Linear,
    // Spherical -- for quaternions, future
}

public enum Reliability
{
    Reliable,    // guaranteed delivery, ordered (default)
    Unreliable   // best-effort, lower latency, good for frequent cosmetic events
}
```

### Usage

```csharp
public class WeaponAspect : IEntityAspect
{
    // State — current value synced via dirty-tick
    [ReplicatedState]
    public readonly ReactiveProperty<int> AmmoInMagazine = new(0);

    [ReplicatedState(Interpolation = InterpolationMode.Linear)]
    public readonly ReactiveProperty<Vector3> Position = new();

    // Event — instant broadcast on each OnNext
    [ReplicatedEvent]
    public readonly Subject<BulletTraceInfo> BulletTraced = new();

    // Cosmetic, frequent — ok to drop
    [ReplicatedEvent(Reliability = Reliability.Unreliable)]
    public readonly Subject<Unit> Footstep = new();

    // Not networked — local only
    public readonly ReactiveProperty<bool> IsHighlighted = new(false);
}
```

### AspectReplicationSystem

Centralized replication system (pure C# class, one per `NetworkManager`). Eliminates
per-entity tick subscriptions, per-tick `byte[]` allocations, and per-entity RPC overhead.

- Subscribes once to `NetworkTickSystem.Tick`
- Maintains `Dictionary<ulong, AspectReplicator>` of registered replicators
- **Server tick**: collects dirty bindings from all replicators, writes one batched
  `FastBufferWriter`, sends via `CustomMessagingManager.SendNamedMessage("ACS_StateBatch")`
  to all non-host clients. Zero managed `byte[]` allocations.
- **Owner tick**: for each pure-client-owner replicator, sends per-entity
  `ACS_OwnerSubmit` to server.
- Events use named messages (`ACS_EventBcast`, `ACS_OwnerEvt`, etc.) instead of RPCs.
- Initial sync: `ACS_SyncReq` / `ACS_SyncReply` named messages.

Named message channels:

| Channel | Direction | Delivery | Purpose |
|---|---|---|---|
| `ACS_StateBatch` | server→clients | Reliable | Batched dirty state per tick |
| `ACS_OwnerSubmit` | owner→server | Reliable | Owner-auth field submission |
| `ACS_EventBcast` / `ACS_EventBcastU` | server→clients | Reliable / Unreliable | Event broadcast |
| `ACS_OwnerEvt` / `ACS_OwnerEvtU` | owner→server | Reliable / Unreliable | Owner event submission |
| `ACS_SyncReq` / `ACS_SyncReply` | client↔server | Reliable | Late-join initial sync |

Wire format for `ACS_StateBatch`:
```
[ushort entityCount]
per entity:
  [ulong  networkObjectId]
  [int    serverTick]         // for interpolation timestamps
  [byte[] dirtyMask]          // (bindingCount+7)/8 bytes
  [bytes  ...fieldPayloads]   // variable, binding-index order
```

### AspectReplicator

Single `NetworkBehaviour` per entity. Must be added to the prefab manually (alongside `NetworkObject`).
NGO requires all `NetworkBehaviour` components to exist on the prefab before spawn —
dynamic `AddComponent` does not work for network replication.

Responsibilities:
- On `OnNetworkSpawn`: scans all aspects in `EntityContext` via reflection, registers with `AspectReplicationSystem`
- For each `[ReplicatedState]` field:
  - Create a `ReplicatedFieldBinding<T>`
  - On authority side: subscribe to `ReactiveProperty<T>` → mark dirty
  - System collects dirty on tick → serializes → sends batched named message
  - On client: system routes incoming message → `ApplyStateBuffer` → write to `ReactiveProperty<T>` with suppression flag
- For each `[ReplicatedEvent]` field:
  - Create a `ReplicatedEventBinding<T>`
  - On authority side: subscribe to `Subject<T>` → serialize → send via `IEventBroadcaster` (named message)
  - On client: system routes → `DispatchEvent` → call `Subject.OnNext(payload)`
- Caches reflection data (same pattern as `AspectInjector`)

### Suppression contract (fields)

`ReplicatedFieldBinding<T>` holds a `_suppressNotification` flag and a
`WriteSuppressed` helper that lifts the flag around an assignment to
`_reactive.Value`. The subscribe callback registered in `SubscribeAsAuthority`
MUST check the flag and bail out when it is set.

Why: when a pure-client owner late-joins, `ACS_SyncReply` delivers a
snapshot that lands through `ApplyStateBuffer → ReadFrom → ApplyFromNetwork
→ WriteSuppressed → _reactive.Value = ...`. Writing to a `ReactiveProperty`
fires the subscribe callback; without the guard, the authority-side
subscription would mark the freshly-applied field as dirty and the next
owner tick would echo the value back to the server — an infinite
relay loop on every late-join.

The same subscribe callback also maintains `OwnerWroteSinceSpawn` (see
`ISSUES.md` #19): the flag is set only when a real authority-side write
lands under `_suppressNotification == false`. `ApplyStateBuffer` uses
the flag in `StateApplyMode.SkipOwnerAuthIfLocallyWritten` mode to decide
whether an incoming initial-sync snapshot may overwrite an owner-auth
field — this is the whole point of the suppression contract: it
distinguishes "applied network state" from "local authority write".

`ReplicatedEventBinding<T>` does NOT carry an equivalent guard — events
were reviewed in batch 3.1 and the suppression path was removed as dead
code. `Subject<T>.OnNext` does not replay, and the authority subscription
routes through a direct broadcaster rather than a reactive write back
into the same subject, so no echo loop is possible. See `ISSUES.md`
history for #12.

### Event binding details

- `Subject<Unit>` is a special case — no payload, just broadcast RPC with no data
- Event payload `T` must satisfy the same type constraints as state (`unmanaged` initially)
- Events do NOT participate in dirty-tick — they are sent immediately when `OnNext` is called
- On host: `OnNext` on server side fires locally; RPC arrives back to host and is skipped (same `IsHost` guard as state)

### Supported Types

Currently only `unmanaged` types (via unsafe byte copy):
- Primitives: `int`, `float`, `bool`, `byte`, `short`, `long`, `double`
- Unity types: `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Color`
- Enums

### TODO: Managed type support

`string`, `FixedString`, and custom `INetworkSerializable` structs require a separate
serialization path (`ReplicatedFieldBinding_Serializable<T>`). The current `where T : unmanaged`
constraint does not allow these types.

### TODO: FastBufferWriter pre-calculation

Currently `FastBufferWriter(256, ...)` with autogrow. Better to pre-calculate buffer size
from `sizeof(T)` of each binding at init time to avoid reallocation.

---

## Layer 1: Authority Modes

### Server Authoritative (default)

- `[ReplicatedState(Authority = AuthorityMode.Server)]`
- Server writes to aspect -> syncs to clients
- Client writes are ignored / blocked on non-authority side
- Used for: health, game state, AI-controlled entities

### Owner Authoritative

- `[ReplicatedState(Authority = AuthorityMode.Owner)]`
- Owner writes to aspect -> syncs to server -> server relays to other clients
- Used for: client-auth co-op games, cosmetic state
- Server can optionally validate (anti-cheat hook)

### Mixing

Different fields can have different authority on the same entity:
```csharp
public class PlayerAspect : IEntityAspect
{
    [ReplicatedState(Authority = AuthorityMode.Server)]
    public readonly Reactive<float> Health = new(100);  // server controls

    [ReplicatedState(Authority = AuthorityMode.Owner)]
    public readonly Reactive<int> SelectedSlot = new(0); // client controls
}
```

---

## Layer 1.5: Component Scope

Problem: with MonoBehaviour-based components, every `EntityComponent` runs on every
peer (host, client, server). That forces manual `if (NetworkManager.Singleton.IsServer)`
checks inside `Update()` for logic that should only run on one side. In DOTS Netcode
this is handled by system groups (`ServerSimulationSystemGroup`) — we need an
equivalent for MonoBehaviour components.

### Attribute

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class NetworkScopeAttribute : Attribute
{
    public NetworkScope Scope { get; }
    public NetworkScopeAttribute(NetworkScope scope) { Scope = scope; }
}

public enum NetworkScope
{
    Everywhere,   // default — runs on server, host, all clients (observers, bridges, VFX)
    ServerOnly,   // only runs on server/host (authoritative game logic, damage)
    OwnerOnly     // only runs on the owning client (local input, camera, HUD)
}
```

### Semantics

- Default (no attribute) = `Everywhere`. Most components are observers/bridges that
  react to replicated state — safe default.
- `ServerOnly` — component is disabled (`enabled = false`) on pure clients.
  On host runs normally (host *is* a server).
- `OwnerOnly` — component is disabled on all peers except the one that owns the
  `NetworkObject`. Useful for input controllers, local cameras, HUDs.

### Usage

```csharp
[NetworkScope(NetworkScope.ServerOnly)]
public class CharacterHealth : EntityComponent
{
    [Aspect] private HealthAspect _health;
    // Damage logic runs only on server — no IsServer checks needed
}

[NetworkScope(NetworkScope.OwnerOnly)]
public class PlayerInputController : EntityComponent
{
    // Input reading runs only on owning client
}

// No attribute → Everywhere (default)
public class MuzzleFlashObserver : EntityComponent
{
    // Reacts to replicated [ReplicatedEvent] Fired → plays VFX on all peers
}
```

### How it's applied

Applied by `AspectReplicator.OnNetworkSpawn()` (or a sibling component):
- Scan all `EntityComponent`s on the entity for `[NetworkScope]` attributes
- Check `IsServer`/`IsOwner` against the scope
- Set `component.enabled = false` on peers where the scope does not match

### Relation to aspect authority

Two levels of enforcement, orthogonal:
- **Data-level** (`AuthorityMode` on `[ReplicatedState]`) — who is allowed to *write*
  to a specific field. Enforced by the replication layer.
- **Logic-level** (`NetworkScope` on component class) — where a component's
  `Update`/`Awake`/etc. runs at all. Enforced by disabling the component.

Both should usually agree (e.g., a `ServerOnly` component writes to `Authority = Server`
fields), but they're separate so you can have an `Everywhere` component that reacts to
replicated state via subscriptions without any authority of its own.

### Mixed components

If a component has both server-only and owner-only logic (e.g., `WeaponController`
that handles input AND fires server-authoritative damage), it must be split into
two components. This is not a cost of this approach — it's how netcode architecture
works in general. The attribute just makes the split explicit instead of hiding it
behind branching inside `Update()`.

---

## Layer 2: Interpolation

For non-owned entities (other players, AI) to appear smooth.

### How It Works

- Client renders remote entities **in the past** (~2 server ticks behind)
- Replicator buffers incoming values with timestamps
- Each frame: lerp between two known snapshots based on render time

```
Server snapshots:   [T=1, pos=0] ---- [T=2, pos=5] ---- [T=3, pos=9]
                                  ^
                    Client renders here (between T=1 and T=2)
                    render_time = 1.6 -> pos = lerp(0, 5, 0.6) = 3.0
```

### Configuration

```csharp
[ReplicatedState(Interpolation = InterpolationMode.Linear)]
public readonly Reactive<Vector3> Position = new();

[ReplicatedState(Interpolation = InterpolationMode.None)]
public readonly Reactive<int> AmmoCount = new();  // discrete, no interpolation
```

### InterpolationBuffer

Per interpolated field:
- Ring buffer of `(tick, value)` pairs
- On `Update()`: calculate render time, find surrounding samples, lerp
- Extrapolation policy: hold last value (no overshooting)

---

## Layer 3: Prediction & Reconciliation

### Input Command

One input struct per game. Contains all possible inputs:

```csharp
public struct PlayerInput : IInputCommand
{
    public Vector2 Move;
    public bool Jump;
    public bool Shoot;
    public float Throttle;
    public float Steer;
}
```

`IInputCommand` is a marker interface (+ `INetworkSerializable` for sending via RPC).

### ISimulate

Components that participate in simulation implement one method:

```csharp
public interface ISimulate
{
    void Simulate(in PlayerInput input, float dt);
}
```

Component example:
```csharp
public class MovementComponent : EntityComponent, ISimulate
{
    [Aspect] private MovementAspect _movement;

    public void Simulate(in PlayerInput input, float dt)
    {
        _movement.Position.Value += input.Move * speed * dt;
        if (input.Jump) _movement.Velocity.Value += Vector3.up * jumpForce;
    }
}
```

### [Predicted] Attribute

Marks fields that participate in prediction/reconciliation snapshot:

```csharp
public class MovementAspect : IEntityAspect
{
    [ReplicatedState(Authority = AuthorityMode.Server)]
    [Predicted]
    public readonly Reactive<Vector3> Position = new();

    [ReplicatedState(Authority = AuthorityMode.Server)]
    [Predicted]
    public readonly Reactive<Vector3> Velocity = new();
}
```

### PredictionManager

Pure C# class, registered in DI (VContainer):

```csharp
public class PredictionManager : IDisposable
{
    private readonly NetworkTickSystem _tickSystem;
    private readonly InputBuffer<PlayerInput> _inputBuffer;
    private readonly List<AspectReplicator> _entities = new();

    public PredictionManager(NetworkTickSystem tickSystem)
    {
        _tickSystem = tickSystem;
        _tickSystem.Tick += OnTick;
    }

    public void Register(AspectReplicator entity);
    public void Unregister(AspectReplicator entity);

    private void OnTick()
    {
        int tick = _tickSystem.ServerTime.Tick;

        // Gather input (owner client only)
        var input = GatherInput();
        _inputBuffer.Store(tick, input);
        SendInputToServer(input, tick);

        // Simulate all predicted entities
        foreach (var entity in _entities)
        {
            entity.Snapshot(tick);
            entity.Simulate(input, _tickSystem.TickDelta);
        }
    }

    // Called when server state arrives via [Replicated] callback
    public void Reconcile(int serverTick)
    {
        int currentTick = _tickSystem.LocalTime.Tick;

        // 1. Apply server state (already written to aspects by replication layer)
        // 2. Replay from serverTick+1 to currentTick using buffered inputs
        for (int t = serverTick + 1; t <= currentTick; t++)
        {
            var input = _inputBuffer.Get(t);
            foreach (var entity in _entities)
                entity.Simulate(input, _tickSystem.TickDelta);
        }
    }

    public void Dispose() => _tickSystem.Tick -= OnTick;
}
```

### Tick System

Uses NGO's built-in `NetworkTickSystem`:
- Tick rate configured in `NetworkManager` inspector
- Server/client tick synchronization handled by NGO
- We subscribe to `Tick` event for fixed simulation step

### Snapshot Buffer

Ring buffer per entity storing [Predicted] field values per tick:

```csharp
public class SnapshotBuffer
{
    private readonly SnapshotData[] _buffer; // size ~128 ticks
    private int _capacity;

    public void Store(int tick, SnapshotData data) => _buffer[tick % _capacity] = data;
    public SnapshotData Get(int tick) => _buffer[tick % _capacity];
}
```

---

## Layer 4: Lag Compensation (future)

Server-side hitbox rollback for shooting/abilities.

- Server stores history of [Predicted] positions per entity (last N ticks)
- When processing a hit: roll back to the tick the shooter saw (based on RTT)
- Check collision against historical positions
- Not an attribute on aspects -- separate server-side system

---

## Layer 5: Optimization (future)

### Delta Compression

- Only send changed [Replicated] fields (dirty flags)
- Reactive<T> already notifies on change -- use this for dirty tracking

### Network Relevancy

- Don't replicate entities too far away
- Per-entity or per-area relevancy
- Could be a component: `NetworkRelevancy { float range; }`

---

## Server vs Client Auth Summary

| Aspect | Server Auth | Client Auth |
|--------|-------------|-------------|
| Attribute | `Authority = Server` | `Authority = Owner` |
| Who calls Simulate | Both (server = truth) | Owner only |
| Prediction needed | Yes | No |
| Reconciliation | Yes | No |
| Input sent to server | Yes (RPC) | No (state sent via Replicated) |
| Anti-cheat | Server validates simulation | Server can validate bounds |

Games can mix both: movement server-auth, inventory client-auth.

---

## Entity Prefab Setup

User only adds what they already know:

```
Entity (root)
|- EntityContext           -- existing
|- NetworkObject           -- required by NGO for any networked entity
|- MovementComponent       -- EntityComponent + ISimulate
|- WeaponComponent         -- EntityComponent + ISimulate
|- HealthComponent         -- EntityComponent (no ISimulate)
```

No extra networking components to add manually.
AspectReplicator is auto-created internally when [Replicated] fields are detected.
Prediction auto-activates when [Predicted] fields are detected.

---

## DI Registration (VContainer)

```csharp
public class NetworkInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<PredictionManager>(Lifetime.Singleton);
    }
}
```

---

## Implementation Order

Status legend: [x] done, [ ] not started, [~] in progress

- [x] **1. [Replicated] state sync (Layer 0)** — server→client sync via `AspectReplicationSystem`
  - `ReplicatedStateAttribute`, `AuthorityMode`, `InterpolationMode` enums
  - `ReplicationScanner` with reflection + caching + stable field sort
  - `ReplicatedFieldBinding<T>` with unsafe unmanaged serialization + feedback-loop guard
  - `AspectReplicator` (`NetworkBehaviour`) — binding scan + state application
  - `AspectReplicationSystem` — centralized tick, batched `CustomMessagingManager` named messages
  - Host excluded from broadcast targets (no self-delivery guard needed)
  - Manual placement on prefab (NGO requires NetworkBehaviours at prefab build time)
  - >256 fields warning (variable-length byte[] mask)

- [x] **2. [ReplicatedEvent] event broadcast (Layer 0.5)** — `Subject<T>` via instant RPC
  - Hard-rename `[Replicated]` → `[ReplicatedState]` (single test consumer, no deprecated alias)
  - `ReplicatedEventAttribute` with `Authority` + `Reliability` props
  - `Reliability` enum (`Reliable` / `Unreliable`)
  - `ReplicatedEventBinding<T>` + factory parallel to `ReplicatedFieldBinding<T>`
  - `ReplicationScanner.ScanEvents` returns `ReplicatedEventInfo[]` sorted by name (stable index)
  - `AspectReplicator` extends:
    - Authority subscribes to `Subject<T>.OnNext` → serialize `sizeof(T)` bytes → broadcast via reliable/unreliable RPC
    - Two RPCs (`BroadcastEventReliableRpc`, `BroadcastEventUnreliableRpc`) because `Delivery` is a compile-time arg
    - Receiver dispatches by `eventIndex` → `ReplicatedEventBinding.ApplyFromNetwork` → `Subject.OnNext` with suppression flag
  - Host guard `if (IsHost) return;` in dispatch (analogous to state)
  - `Subject<Unit>` goes through the shared `unmanaged` path (1-byte overhead, acceptable)
  - Note: only `AuthorityMode.Server` is functional; `Owner` path shares the gap with state and lands in step 4
  - Note: event index is `byte` → ≤256 events/entity (warning if exceeded)

- [x] **3. Component Scope (Layer 1.5)** — `[NetworkScope]` class-level attribute
  - `NetworkScopeAttribute` + `NetworkScope` enum (`Everywhere` / `ServerOnly` / `OwnerOnly`)
  - `NetworkScopeScanner` with per-type cache (parallel to `ReplicationScanner`)
  - `AspectReplicator.OnNetworkSpawn` calls `ApplyNetworkScopes()` first — scans via
    `GetComponentsInChildren<IEntityComponent>(includeInactive: true)` so components on
    Visual children are covered, not only the root
  - `ServerOnly` → `behaviour.enabled = IsServer`; `OwnerOnly` → `behaviour.enabled = IsOwner`
  - `OnGainedOwnership` / `OnLostOwnership` re-apply on the cached `OwnerOnly` array
  - Removes most `if (IsServer)` / `if (IsOwner)` checks from user code
  - Note: NGO does not guarantee `OnNetworkSpawn` order between `NetworkBehaviour`s on
    the same `NetworkObject` — an `EntityNetworkComponent` may have already subscribed
    before we disable it. `Update` is still suppressed and its `DisposableBag` releases
    on `OnNetworkDespawn`. Acceptable for MVP.

- [x] **4. AuthorityMode.Owner (Layer 1)** — client-auth flow
  - Owner writes → `SubmitOwnerStateRpc` (`SendTo.Server, InvokePermission = RpcInvokePermission.Owner`) → server applies + `MarkDirty` → existing `OnServerTick` relays via `BroadcastStateRpc` to `NotServer`
  - Pure client owner subscribes `NetworkTickSystem.Tick += OnOwnerTick`; host-owner is handled by `OnServerTick` (single broadcast path, no extra owner hop)
  - Events: `SubmitOwnerEventReliableRpc`/`SubmitOwnerEventUnreliableRpc` → server relays via `BroadcastEventReliableRpc`/`BroadcastEventUnreliableRpc` and fires locally on server-side
  - Per-field / per-event authority skip on receive: pure client owner skips `Owner` fields in `BroadcastStateRpc` (via new `ReplicatedFieldBinding.Skip(FastBufferReader)`) and `Owner` events in `DispatchEvent`, preventing a relay race from overwriting a fresher local write
  - Anti-cheat: framework-level via `RpcInvokePermission.Owner` (NGO rejects RPCs not from the NetworkObject's owner); server-side per-field/per-event check that `Authority == Owner` rejects malformed payloads
  - Single dirty bitmask shared between server-auth and owner-auth fields; per-field `AuthorityMode[]` parallel array on replicator distinguishes them at tick/receive time
  - Note: runtime ownership changes for replication are not covered here — bindings are captured at `OnNetworkSpawn` and owner-tick subscription is not re-evaluated in `OnGainedOwnership` / `OnLostOwnership` (`[NetworkScope]` still re-applies). Follow-up step.

- [x] **5. InterpolationBuffer (Layer 2)** — smooth remote entities
  - `Interpolators` registry with per-type lerpers: `float`, `double`, `Vector2/3/4`, `Quaternion` (Slerp), `Color`
  - `InterpolatedFieldBinding<T>` subclass of `ReplicatedFieldBinding<T>` with ring buffer (32 snapshots) keyed by server tick
  - `BroadcastStateRpc` payload prefixed with `int serverTick` so snapshot timestamps stay monotonic even when multiple RPCs land in one frame
  - `AspectReplicator.Update()` lerps at `renderTime = ServerTime.Time - 2 * tickInterval`; cached `_interpolatedBindings` array, early-return when empty
  - Authority bypass: `shouldInterpolate = !IsServer && !isAuthority && Linear` — host / server / owner of owner-auth fields never buffer
  - First-snapshot bootstrap: first received value is applied immediately so the entity does not stall at `default(T)` for the delay window
  - Unsupported types (int / bool / enum) with `Linear` → one-time warning + fallback to immediate apply
  - `ApplyFromNetwork()` signature changed to `ApplyFromNetwork(double receivedTime)`; `SubmitOwnerStateRpc` passes `0` (server does not interpolate)

- [ ] **6. IInputCommand + ISimulate + PredictionManager (Layer 3)** — prediction loop
  - Marker interface `IInputCommand : INetworkSerializable`
  - `ISimulate { void Simulate(in TInput input, float dt); }`
  - `PredictionManager` as pure C# DI singleton subscribing to `NetworkTickSystem.Tick`
  - `[Predicted]` attribute marking fields included in snapshot

- [ ] **7. SnapshotBuffer + reconciliation (Layer 3)** — rollback machinery
  - Per-entity ring buffer of `[Predicted]` field values keyed by tick
  - On server state arrival: restore snapshot → replay inputs from serverTick+1 to currentTick

- [ ] **8. Lag compensation (Layer 4)** — server-side hitbox rollback
- [ ] **9. Delta compression + relevancy (Layer 5)** — optimization

### TODO (cross-cutting, not tied to a specific layer)

- [ ] Managed type support for replication (`string`, `FixedString`, `INetworkSerializable`) — requires `ReplicatedFieldBinding_Serializable<T>` alternate path
- [ ] `FastBufferWriter` pre-calculation from `sizeof(T)` of bindings at init time (avoid autogrow reallocation)
- [ ] IL2CPP: document `[Preserve]` requirement for `MakeGenericType` with value types
- [x] Replace per-tick `byte[]` RPC allocation with `CustomMessagingManager` (batch 3.6, 2026-04-10)
- [ ] Runtime ownership changes: rebuild owner-auth bindings and owner-tick subscription in `OnGainedOwnership` / `OnLostOwnership` so possession-style handoff works mid-session
