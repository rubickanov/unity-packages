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

### Attributes

```csharp
[AttributeUsage(AttributeTargets.Field)]
public class ReplicatedAttribute : Attribute
{
    public AuthorityMode Authority { get; set; } = AuthorityMode.Server;
    public InterpolationMode Interpolation { get; set; } = InterpolationMode.None;
}

public enum AuthorityMode
{
    Server,  // server writes, clients read
    Owner    // owner writes, server relays to others
}

public enum InterpolationMode
{
    None,
    Linear,
    // Spherical -- for quaternions, future
}
```

### Usage

```csharp
public class HealthAspect : IEntityAspect
{
    [Replicated]
    public readonly Reactive<float> Current = new(100);

    [Replicated]
    public readonly Reactive<float> Max = new(100);

    // Not replicated -- local only
    public readonly Reactive<bool> IsFlashing = new(false);
}
```

### AspectReplicator (internal, auto-created)

Single `NetworkBehaviour` per entity. Auto-added by `EntityContext` when it detects
any `[Replicated]` fields in aspects. User never adds this manually.

- On `OnNetworkSpawn`: scans all aspects in `EntityContext` via reflection
- Finds all fields marked `[Replicated]`
- Creates `NetworkVariable<T>` for each field dynamically
- Sets up sync:
  - **Authority side** (server or owner): subscribes to `Reactive<T>` changes -> writes to `NetworkVariable<T>`
  - **Non-authority side**: subscribes to `NetworkVariable<T>.OnValueChanged` -> writes to `Reactive<T>`
- If any `[Predicted]` fields found: auto-registers entity in `PredictionManager`
- If any `ISimulate` components found: collects them for simulation calls
- Caches reflection data (same pattern as `AspectInjector`)

```csharp
internal class AspectReplicator : NetworkBehaviour
{
    private List<ReplicatedField> _fields;
    private ISimulate[] _simulators;
    private SnapshotBuffer _snapshots;

    public override void OnNetworkSpawn()
    {
        var context = GetComponent<EntityContext>();
        _fields = ReflectionScanner.FindReplicatedFields(context);

        foreach (var field in _fields)
        {
            field.CreateNetworkVariable(this);
            field.Bind(IsServer, IsOwner);
        }

        // Auto-detect prediction
        if (_fields.Any(f => f.IsPredicted))
        {
            _simulators = GetComponentsInChildren<ISimulate>();
            _snapshots = new SnapshotBuffer(128);
            PredictionManager.Instance.Register(this);
        }
    }

    public override void OnNetworkDespawn()
    {
        PredictionManager.Instance?.Unregister(this);
    }
}
```

### Auto-Detection Flow

```
EntityContext.Awake()
  1. Initialize aspects (existing)
  2. Scan aspects for [Replicated] fields
  3. If any found:
     a. AddComponent<AspectReplicator>() (if not already present)
     b. AspectReplicator handles everything from OnNetworkSpawn
```

### Supported Types

Must handle serialization for:
- Primitives: `int`, `float`, `bool`
- Unity types: `Vector2`, `Vector3`, `Quaternion`, `Color`
- Enums
- Custom `INetworkSerializable` structs

---

## Layer 1: Authority Modes

### Server Authoritative (default)

- `[Replicated(Authority = AuthorityMode.Server)]`
- Server writes to aspect -> syncs to clients
- Client writes are ignored / blocked on non-authority side
- Used for: health, game state, AI-controlled entities

### Owner Authoritative

- `[Replicated(Authority = AuthorityMode.Owner)]`
- Owner writes to aspect -> syncs to server -> server relays to other clients
- Used for: client-auth co-op games, cosmetic state
- Server can optionally validate (anti-cheat hook)

### Mixing

Different fields can have different authority on the same entity:
```csharp
public class PlayerAspect : IEntityAspect
{
    [Replicated(Authority = AuthorityMode.Server)]
    public readonly Reactive<float> Health = new(100);  // server controls

    [Replicated(Authority = AuthorityMode.Owner)]
    public readonly Reactive<int> SelectedSlot = new(0); // client controls
}
```

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
[Replicated(Interpolation = InterpolationMode.Linear)]
public readonly Reactive<Vector3> Position = new();

[Replicated(Interpolation = InterpolationMode.None)]
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
    [Replicated(Authority = AuthorityMode.Server)]
    [Predicted]
    public readonly Reactive<Vector3> Position = new();

    [Replicated(Authority = AuthorityMode.Server)]
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

1. **[Replicated] + AspectReplicator (auto-created)** -- basic server->client sync
2. **AuthorityMode.Owner** -- client-auth support
3. **InterpolationBuffer** -- smooth remote entities
4. **IInputCommand + ISimulate + PredictionManager** -- prediction/reconciliation
5. **SnapshotBuffer + reconciliation loop** -- snapshot/rollback machinery
6. **Lag compensation** -- server-side hitbox history
7. **Delta compression + relevancy** -- optimization
