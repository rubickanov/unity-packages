# ACS Netcode

Declarative replication, prediction, and events for [ACS](../com.rubickanov.acs/) over Unity Netcode for GameObjects. Mark aspect fields with `[Replicated]` / `[ReplicatedEvent]`; the replicator discovers them at spawn and drives tick-batched state, owner submissions, broadcast events, and rollback-replay prediction without hand-written RPCs.

## Dependencies

- `com.rubickanov.acs` — aspect/component entities. Replicated fields live on aspects.
- `com.unity.netcode.gameobjects` — underlying transport, `NetworkObject` / `NetworkBehaviour` lifecycle, named messages.
- `R3` — `ReactiveProperty<T>` and `Subject<T>` are the supported field shapes.

## Architecture

```
Aspect field
    │   [Replicated] / [ReplicatedEvent]
    ▼
ReplicationScanner ──► ReplicatedFieldBinding<T>         ReplicatedEventBinding<T>
                              │                                  │
                              ▼                                  ▼
                        AspectReplicator  ◄── [NetworkScope] ──► component enable/disable
                              │                                  │
                              ▼                                  ▼
                       AspectReplicationSystem (singleton per NetworkManager)
                              │        │        │
                  ACS_StateBatch   ACS_EventBcast   ACS_OwnerSubmit / ACS_OwnerEvt
                              │
                              ▼
                      PredictionManager<TInput>    (ISimulate / IInputProvider)
```

`AspectReplicator` sits on the networked prefab root next to `MonoEntity`. On `OnNetworkSpawn` it scans every aspect, builds per-field bindings (raw memcpy or quantizing codec, plain / interpolated / authority-smoothed) and per-event bindings, registers with the `NetworkManager`-wide `AspectReplicationSystem`, and applies `[NetworkScope]` to sibling components. `AspectReplicationSystem` runs one `OnTick` per frame: it batches every dirty field across every replicator into a single `ACS_StateBatch` message; owner-auth fields go out through `ACS_OwnerSubmit`; events flow through broadcast / owner-submit channels with per-event reliability. Prediction adds a second manager (`PredictionManager<TInput>`) that submits inputs, runs `ISimulate` on both owner and server, and rewinds-and-replays on the pure-client owner when an authoritative batch arrives.

## Core Concepts

**`[Replicated]`** — Marks a `ReactiveProperty<T>` field on an aspect for per-tick state sync. The authority side's writes are delta-sent (dirty-mask); non-authority peers receive the value and write it into the same reactive. Configures authority, interpolation, prediction, and quantization.

**`[ReplicatedEvent]`** — Marks a `Subject<T>` field on an aspect. Each `OnNext` on the authority is serialized into a fire-and-forget RPC and re-fired on every other peer's local `Subject`. No replay, no buffering — missed subscribers miss the event. Reliability is configurable per field.

**`AuthorityMode`** — `Server` (default) or `Owner`. Server-auth fields are written on the server and broadcast to clients. Owner-auth fields are written on the owning client, relayed through the server to everyone else.

**`NetworkScope`** — Class-level attribute on `IEntityComponent` types. `Everywhere` (default), `ServerOnly`, or `OwnerOnly`. Components whose scope does not match the current peer are set `enabled = false` in `OnNetworkSpawn` (and re-evaluated on ownership change for `OwnerOnly`).

**`InterpolationMode`** — Per-field. `Linear` enables snapshot-buffered interpolation on receiving peers plus authority-side render smoothing on writing peers. Read via `.Smooth()`; `.Value` remains the raw latest tick.

**`QuantizationMode`** — Per-field lossy compression. `HalfPrecision` (float / Vector2..4), `SmallestThree` (Quaternion → 4 bytes). Invalid combinations throw at scan time.

**Prediction** — A `[Replicated(Predicted = true)]` server-auth field on an entity with an `ISimulate<TInput>` component becomes a predicted field. The owning client runs `Simulate` locally each tick, submits input to the server, and rewinds/replays on reconcile; the server runs `Simulate` as the authority.

## Quick Start

1. Make sure `NetworkManager` has a non-zero `NetworkTickSystem.TickRate` in its `NetworkConfig`. A tick rate of `0` disables replication with an error log.
2. Add `NetworkObject` + `MonoEntity` + `AspectReplicator` to the prefab root. Set `Interpolation Delay Ticks` on the replicator (default `2` — lower is snappier, higher masks packet jitter).
3. Mark aspect fields with `[Replicated]` / `[ReplicatedEvent]`:

```csharp
public class HealthAspect : IEntityAspect
{
    [Replicated]
    public readonly ReactiveProperty<int> CurrentHealth = new(100);

    [ReplicatedEvent]
    public readonly Subject<DamageInfo> Hit = new();
}
```

4. Write components as usual. Reactions run on every peer (because reactive fields fire on every peer that applies the incoming value):

```csharp
public class HealthHudBinder : EntityNetworkComponent
{
    [Aspect] private HealthAspect _health = default!;

    protected override void OnSubscribe(ref DisposableBag disposables)
    {
        _health.CurrentHealth.Subscribe(v => _hud.SetHealth(v)).AddTo(ref disposables);
        _health.Hit.Subscribe(info => _hud.FlashDamage(info)).AddTo(ref disposables);
    }
}
```

Server code (wherever your gameplay logic lives) writes `_health.CurrentHealth.Value -= damage;` and the value lands on every client.

## Usage

### Replicating Fields

Only `ReactiveProperty<T>` where `T` is an unmanaged type is supported. The scanner rejects `IObservableCollection<T>` fields with a targeted error — use a local subscription + custom RPC for networked collections until native delta support lands.

```csharp
public class MovementAspect : IEntityAspect
{
    [Replicated(Interpolation = InterpolationMode.Linear, Quantization = QuantizationMode.HalfPrecision)]
    public readonly ReactiveProperty<Vector3> Position = new(Vector3.zero);

    [Replicated(Interpolation = InterpolationMode.Linear, Quantization = QuantizationMode.SmallestThree)]
    public readonly ReactiveProperty<Quaternion> Rotation = new(Quaternion.identity);

    [Replicated] // default: server-auth, no interpolation, raw bytes
    public readonly ReactiveProperty<float> MoveSpeed = new(5f);
}
```

### Replicating Events

Use a `Subject<T>` for fire-and-forget notifications. `Reliability.Reliable` is the default; switch to `Unreliable` for high-frequency cosmetic events where a drop is imperceptible:

```csharp
public class CombatAspect : IEntityAspect
{
    [ReplicatedEvent] // gameplay-critical: reliable + ordered
    public readonly Subject<DamageInfo> Hit = new();

    [ReplicatedEvent(Reliability = Reliability.Unreliable)] // cosmetic spam
    public readonly Subject<Vector3> Footstep = new();

    [ReplicatedEvent(Authority = AuthorityMode.Owner)] // owner fires, server relays
    public readonly Subject<string> Emote = new();
}
```

Rule of thumb: if the player would notice a *single* missed instance, pick `Reliable`. If they would only notice *all* of them missing, pick `Unreliable`.

### Authority

Server authority is the default. Flip to owner authority when the owning client is the single source of truth (chat, voice state, local loadout picks, anything input-driven that does not participate in prediction):

```csharp
public class ChatAspect : IEntityAspect
{
    [Replicated(Authority = AuthorityMode.Owner)]
    public readonly ReactiveProperty<bool> IsTyping = new(false);

    [ReplicatedEvent(Authority = AuthorityMode.Owner)]
    public readonly Subject<string> SendMessage = new();
}
```

For input-driven gameplay state (position, velocity, cooldowns) prefer server authority + prediction — see below. Owner authority has no reconcile path: if the owner and server disagree, the owner wins until the object changes hands.

### Network Scope

Keep components silent on peers where they have nothing to do:

```csharp
[NetworkScope(NetworkScope.OwnerOnly)]
public class LocalPlayerInput : EntityNetworkComponent { /* ... */ }

[NetworkScope(NetworkScope.ServerOnly)]
public class AiDecisionMaker : EntityNetworkComponent { /* ... */ }

// [NetworkScope(NetworkScope.Everywhere)] is the default — omit the attribute.
public class HealthHudBinder : EntityNetworkComponent { /* ... */ }
```

`AspectReplicator` flips `enabled = false` on mismatched components during `OnNetworkSpawn`. `OwnerOnly` is re-evaluated on `OnGainedOwnership` / `OnLostOwnership` so local HUDs and input readers follow the current owner.

### Networked Components

Extend `EntityNetworkComponent` instead of `EntityComponent` when you need `NetworkBehaviour` capabilities (RPCs, `NetworkVariable`, `IsServer` / `IsOwner` checks, `OnNetworkSpawn`). Aspect injection and `OnSubscribe` still work — `[Aspect]` fields resolve in `Awake`, and `OnSubscribe` is called once both `OnNetworkSpawn` has fired *and* the component is enabled:

```csharp
[NetworkScope(NetworkScope.ServerOnly)]
public class RespawnAuthority : EntityNetworkComponent
{
    [Aspect] private HealthAspect _health = default!;

    protected override void OnSubscribe(ref DisposableBag disposables)
    {
        _health.CurrentHealth
            .Where(v => v <= 0)
            .Subscribe(_ => ScheduleRespawn())
            .AddTo(ref disposables);
    }
}
```

A plain `EntityComponent` is still the right choice for components that do not need `NetworkBehaviour` — replication works either way because replication reads from the aspect, not the component.

### Smooth Rendering

Tick-rate state looks like a staircase at 60+ FPS. Call `.Smooth()` in visual code to read the interpolated value; game logic keeps reading `.Value`:

```csharp
public class PositionSync : EntityComponent
{
    [Aspect] private MovementAspect _movement = default!;

    private void LateUpdate()
    {
        transform.position = _movement.Position.Smooth();
        transform.rotation = _movement.Rotation.Smooth();
    }
}
```

`.Smooth()` falls back to `.Value` when the field has no `InterpolationMode.Linear` or when no interpolation is active on this peer. Safe to call unconditionally.

On non-authority peers `.Smooth()` is snapshot-buffered (delayed by `Interpolation Delay Ticks` to mask jitter). On the authority peer — and on the pure-client owner of a predicted field — it is wall-clock-smoothed between local writes so local motion renders frame-rate-smooth instead of tick-rate-snapped.

### Client-Side Prediction

Define an unmanaged input struct:

```csharp
public struct MoveInput : IInputCommand
{
    public Vector2 Move;
    public bool Jump;
}
```

Implement `IInputProvider<TInput>` on the owner (typically `OwnerOnly`-scoped) and `ISimulate<TInput>` on a component that lives on both server and owner. Mark the mutated fields `Predicted = true`:

```csharp
public class MovementAspect : IEntityAspect
{
    [Replicated(Interpolation = InterpolationMode.Linear, Predicted = true,
                Quantization = QuantizationMode.HalfPrecision)]
    public readonly ReactiveProperty<Vector3> Position = new(Vector3.zero);
}

[NetworkScope(NetworkScope.OwnerOnly)]
public class PlayerInputProvider : EntityNetworkComponent, IInputProvider<MoveInput>
{
    public MoveInput Gather() => new()
    {
        Move = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")),
        Jump = Input.GetButton("Jump"),
    };
}

public class CharacterMover : EntityNetworkComponent, ISimulate<MoveInput>
{
    [Aspect] private MovementAspect _movement = default!;

    public void Simulate(in MoveInput input, float dt)
    {
        // Runs on server (authority) AND on the pure-client owner (prediction).
        // Writes to [Replicated(Predicted = true)] fields propagate through normal replication.
        _movement.Position.Value += new Vector3(input.Move.x, 0, input.Move.y) * 5f * dt;
    }
}
```

`PredictionManager<TInput>` is created automatically the first time a predicted entity spawns. Owner sends input each tick; server runs the tick-aligned input through `Simulate`; owner also runs `Simulate` locally and captures a snapshot per tick. When the authoritative state for tick `t` arrives, the owner replays inputs `t+1..now` on top of it so the local view smoothly re-converges instead of snapping back.

`Predicted = true` requires `Authority = AuthorityMode.Server`. Predicting an owner-auth field is a no-op (the owner is already the source of truth); the scanner clears the flag with a warning.

### Compatibility

Server and client must be built from the same commit. The wire format has no version negotiation — running peers with different `[Replicated]` field sets against each other silently corrupts state because field indices shift. If you need rolling upgrades or cross-version matchmaking, this package is not the right fit.

Field count cap per `AspectReplicator` is 256 (one dirty-mask byte per 8 fields, hard-limited); event count cap is 256 (one-byte event index). The replicator aborts spawn with an error if either is exceeded.

### IL2CPP

Built-in unmanaged types (`int`, `float`, `bool`, `double`, `Vector2..4`, `Quaternion`, `Color`) are preserved by `AotHints` and work on IL2CPP out of the box. For custom unmanaged structs — your own `IInputCommand` or aspect field types — add a `link.xml` to `Assets/` so IL2CPP keeps the closed generic specializations:

```xml
<linker>
  <assembly fullname="ACS.Runtime.Netcode" preserve="all"/>
</linker>
```

## Examples

### Predicted Player Character

Classic FPS-style movement: server authority, owner prediction, half-precision position, smallest-three rotation.

```csharp
public class PlayerAspect : IEntityAspect
{
    [Replicated(Interpolation = InterpolationMode.Linear, Predicted = true,
                Quantization = QuantizationMode.HalfPrecision)]
    public readonly ReactiveProperty<Vector3> Position = new(Vector3.zero);

    [Replicated(Interpolation = InterpolationMode.Linear, Predicted = true,
                Quantization = QuantizationMode.SmallestThree)]
    public readonly ReactiveProperty<Quaternion> Rotation = new(Quaternion.identity);

    [Replicated] // server-only write; no prediction, no interpolation
    public readonly ReactiveProperty<int> CurrentHealth = new(100);

    [ReplicatedEvent(Reliability = Reliability.Unreliable)]
    public readonly Subject<Vector3> Footstep = new();
}
```

Server and owner both run `CharacterMover.Simulate` each tick. Observer clients receive tick-batched `Position` / `Rotation` updates and render them via `.Smooth()` with the replicator's interpolation delay. Health is written only on the server (e.g. by a damage component) and mirrors to everyone without prediction. `Footstep` is fire-and-forget — a dropped packet silently drops one step, which nobody notices.

### Server-Driven NPC

Same aspect, different attribution — no `Predicted`, no `InputProvider`, server runs whatever AI component writes the fields:

```csharp
public class NpcAspect : IEntityAspect
{
    [Replicated(Interpolation = InterpolationMode.Linear, Quantization = QuantizationMode.HalfPrecision)]
    public readonly ReactiveProperty<Vector3> Position = new(Vector3.zero);

    [Replicated(Interpolation = InterpolationMode.Linear, Quantization = QuantizationMode.SmallestThree)]
    public readonly ReactiveProperty<Quaternion> Rotation = new(Quaternion.identity);

    [ReplicatedEvent]
    public readonly Subject<DamageInfo> Hit = new();
}

[NetworkScope(NetworkScope.ServerOnly)]
public class NpcBrain : EntityNetworkComponent { /* AI writes NpcAspect fields */ }
```

Observer clients interpolate between incoming snapshots, delayed by `Interpolation Delay Ticks` ticks.

### Owner-Auth Chat

Local state that should never be authoritative on the server — typing indicator flips on the owner, relays to everyone:

```csharp
public class ChatAspect : IEntityAspect
{
    [Replicated(Authority = AuthorityMode.Owner)]
    public readonly ReactiveProperty<bool> IsTyping = new(false);

    [ReplicatedEvent(Authority = AuthorityMode.Owner)]
    public readonly Subject<string> SendMessage = new();
}

[NetworkScope(NetworkScope.OwnerOnly)]
public class LocalChatInput : EntityNetworkComponent
{
    [Aspect] private ChatAspect _chat = default!;

    public void Type(bool typing) => _chat.IsTyping.Value = typing;
    public void Send(string msg) => _chat.SendMessage.OnNext(msg);
}
```

Owner writes the reactive; the state batch goes to the server through `ACS_OwnerSubmit` and fans out to the other clients via the normal broadcast. `SendMessage` hops owner → server → all other clients.

## Design Decisions

- **Attributes on aspects, not on components** — replication describes *data shape*, not *behaviour*. Placing the attribute on the aspect keeps components stateless and lets the same aspect be consumed by networked and non-networked components interchangeably.
- **Single `AspectReplicationSystem` per `NetworkManager`** — one tick subscription, one batched message per frame instead of one RPC per dirty field per entity. Named messages over `CustomMessagingManager` bypass the generator-based RPC path so there are no per-entity managed allocations on the hot path.
- **Dirty-mask bit is positional, not named** — wire bytes are `(serverTick + mask + payloads in binding-index order)`. Cheap and compact, but couples both peers to the same field set. This is the "same commit on both sides" constraint.
- **`AuthorityRenderBinding` smooths authority writes against wall-clock** — without it, `.Smooth()` on the authority (or the predicted owner) falls back to `.Value`, which updates only at tick rate and staircases visibly at 60+ FPS. Costs ≈1 tick of render delay on the authority — the same tradeoff Unity's `NetworkTransform` makes.
- **Prediction is opt-in per field, not per entity** — `Predicted = true` on the exact fields the owner writes via `Simulate`. Non-predicted state (health, ability cooldowns tied to server-only effects) flows through the normal server-auth path and lands one RTT late — which is correct, because the owner should not be speculating about those.
- **Owner-auth has no reconcile** — there is no "rewind and re-apply" for owner-auth fields, because the owner *is* the authority. If you need server-correctable state, use server authority + prediction instead.
- **`ReplicatedEvent` does not replay late-joiners** — events are fire-and-forget. Use a `[Replicated]` field if a late-joining client needs to observe the current state (e.g. "is typing" as a bool, not "started typing" as an event).
