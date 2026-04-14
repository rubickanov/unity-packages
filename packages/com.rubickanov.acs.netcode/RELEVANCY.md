# Relevancy — Design & Plan

Status: **planned, not implemented.**

This document locks in the scope and design decisions so implementation can
proceed in a single focused pass.

---

## Context: what's already in place

Two layers of compression already ship:

- **Field-level dirty delta.** `EntityReplicationSystem.ServerTick` writes a
  per-entity `dirtyMask` and only emits bytes for dirty bindings — the wire
  is delta-compressed at field granularity.
- **Value-level quantization (opt-in).** `[Replicated(Quantization = ...)]`
  selects a per-field codec via `IFieldCodec<T>`. `HalfPrecision` halves
  float/Vector2/3/4, `SmallestThree` quarters `Quaternion`. `payloadBytes`
  per-entity prefix tracks actual codec output, so quantized fields
  automatically shrink the packet.

What's **not** in place:

- **Per-client filtering.** A single `ACS_StateBatch` is sent to every
  non-host client via `_broadcastTargetIds`. Irrelevant entities ride along.
- **Entity-level subscription.** There's no concept of "client C does not
  need entity E at all."

Relevancy closes both gaps.

---

## Scope decision

**In scope for this iteration:**

- Per-entity relevancy policy (distance-based default).
- Per-client state batching in `EntityReplicationSystem`.
- Per-client event filtering.
- Integration with NGO's `NetworkObject.Observers` / `NetworkShow` / `NetworkHide`
  so spawn/despawn and initial sync flow through the existing pipeline.

**Out of scope (deferred, revisit only if profiler demands):**

- Baseline-acked delta (Quake-3 snapshot model). Large complexity, small
  incremental win after field-dirty + relevancy + quantization. Not planned.
- Range-based fixed-point quantization (`int16` with configurable world
  range, e.g. ±500m / 0.015m step). Complementary to the current
  half-float preset — useful only when half-float precision degrades at
  large world coordinates. Add as a new `QuantizationMode` entry if it
  ever matters.
- Spatial hashing / grid buckets for relevancy queries. Naive O(entities ×
  clients) per relevancy tick is fine below ~50 entities × ~8 clients. Above
  that, see "Scaling notes" at the end.
- Cell/sector-based interest management (world grid with subscription per
  cell). Distance-based covers typical co-op and small PvP. Revisit for
  large open-world or high-entity-density games.

---

## Design

### Relevancy policy abstraction

```csharp
public interface IRelevancyPolicy
{
    // Called per (entity, client) pair on the relevancy tick.
    bool IsRelevantTo(ulong clientId, NetworkObject entity);
}
```

Pluggable. Default implementation:

```csharp
public sealed class DistanceRelevancy : IRelevancyPolicy
{
    public float ShowRadius = 50f;   // start showing when inside this
    public float HideRadius = 55f;   // stop showing when outside this (hysteresis)

    public DistanceRelevancy(IClientFocus focus) { /* ... */ }
}
```

Hysteresis (`HideRadius > ShowRadius`) prevents flicker when an entity sits
right on the boundary.

### Client focus

"Where is client C looking from?" abstracted via:

```csharp
public interface IClientFocus
{
    bool TryGetFocusPosition(ulong clientId, out Vector3 position);
}
```

Default implementation: return `transform.position` of the first
`NetworkObject` owned by that client. Game-specific implementations can
override (third-person camera, spectator camera, multiple-perspective
setups).

Registered as a singleton per `NetworkManager`, similar to
`EntityReplicationSystem` / `PredictionManager`. Lookup resolves on first
use; absence falls back to "everything relevant" with a one-time warning.

### NetworkRelevancy component

Opt-in on entities that should participate in relevancy:

```csharp
public class NetworkRelevancy : MonoBehaviour
{
    [SerializeField] float showRadius = 50f;
    [SerializeField] float hideRadius = 55f;
    // future: override policy per-entity
}
```

**Entities WITHOUT this component stay globally visible** (current behavior).
Useful for world-state aspects, singletons, UI-backing entities that all
clients must see.

### RelevancySystem

Per-`NetworkManager` singleton, mirrors `EntityReplicationSystem` lifecycle:

- Subscribes to `NetworkTickSystem.Tick`; runs the relevancy check every
  Nth tick where `N = round(tickRate / 10)` (≈10 Hz regardless of sim rate).
- Maintains a registry of `NetworkRelevancy` entities.
- On each relevancy tick, for every `(entity, connectedClient)` pair:
  - Compute `isRelevantNow` via policy.
  - Compare against stored `wasRelevant` bit.
  - On `false → true`: `networkObject.NetworkShow(clientId)`.
  - On `true → false`: `networkObject.NetworkHide(clientId)`.
- Per-entity state: `Dictionary<ulong, bool>` (clientId → last-known
  relevance). Preallocated; cleaned up in `OnClientDisconnect`.

The system does **not** do distance math itself — it delegates to the
policy. Keeps the tick loop agnostic of geometry and trivially pluggable.

### Per-client state batching

`EntityReplicationSystem.ServerTick` restructures from "one batch for all"
to "one batch per client":

```
1. For each entity in _iterationSnapshot:
     compute dirty mask (once, shared across clients)

2. For each connected client (skip host local):
     create FastBufferWriter
     write ushort entityCount placeholder
     count = 0
     for each dirty entity:
         if entity.NetworkObject.IsNetworkVisibleTo(clientId):
             write (networkObjectId, serverTick, mask, payloads)
             count++
     patch entityCount = count
     if count > 0: send ACS_StateBatch to clientId

3. After all clients processed:
     for each dirty entity: ClearDirty() on all bindings
```

Key points:

- **Dirty mask computed once per entity**, reused across clients. Otherwise
  we'd recompute N_clients times for no benefit.
- **`ClearDirty()` runs only after the last client is served.** Clearing
  after the first client would silently drop data for the second.
- **`IsNetworkVisibleTo` is O(1) on NGO's observer set** (HashSet lookup).
  Per-tick cost scales as O(dirty_entities × clients), not O(all_entities
  × clients).
- Entities without `NetworkRelevancy` have their NetworkObject observers =
  all-clients by NGO default, so they land in every batch automatically.
  No special-case branch needed.

### Events

`IEventBroadcaster.SendEvent` currently targets `_broadcastTargetIds`
(all non-host). Change: target = `entity.NetworkObject.Observers` minus host.

Non-observers drop the event silently. Example: distant enemy fires
`[ReplicatedEvent] Shout` — clients who don't see the enemy never get the
event. Design decision confirmed: drop is correct; global events belong
on a singleton aspect without `NetworkRelevancy`.

### Initial sync on show

The flow when `NetworkShow(clientId)` triggers:

1. NGO spawns the `NetworkObject` on the client.
2. Client's `EntityReplicator.OnNetworkSpawn` fires.
3. It calls `EntityReplicationSystem.RequestInitialSync(this)` → sends
   `ACS_SyncReq` to server.
4. Server replies with `ACS_SyncReply` containing full state.

**This should Just Work** without changes — the existing late-join path
is reused for "became relevant to this client." Verify with an integration
test: entity starts far, client approaches, check that the client sees
the current state not `default(T)`.

### Ownership transfer edge case

If an entity's owner client is made non-observer of that entity (owner
moves out of their own relevancy bubble — unusual but possible), owner-auth
behavior would break. Guard: **owner is always an observer of owned
entities.** Enforce in `RelevancySystem` by short-circuiting the policy
for `clientId == entity.OwnerClientId`.

### Host handling

Host is server + client. The existing `_broadcastTargetIds` filter already
excludes host local client from state sends. Keep that filter — host reads
authoritative state directly, no need to receive its own batch.

For the **spawn/show** side: host automatically observes every entity via
NGO's default (host is the server, sees everything). Relevancy system
should not attempt to `NetworkHide` for host. Guard:
`if (clientId == NetworkManager.ServerClientId && IsHost) continue;`

---

## Implementation order

Proposed commit granularity:

1. **`IClientFocus` + default `OwnedObjectFocus` impl.** Standalone, testable
   with fakes.
2. **`IRelevancyPolicy` + `DistanceRelevancy` impl.** Unit-testable against
   fake focus.
3. **`NetworkRelevancy` component.** Pure data carrier, no logic.
4. **`RelevancySystem` singleton.** Hooks `NetworkTickSystem.Tick`, iterates
   pairs, calls `NetworkShow`/`NetworkHide`. Integration test: entity
   starts far, client approaches, `Observers` set updates.
5. **Per-client batching in `ServerTick`.** Refactor the one-batch loop
   into per-client, gated by `IsNetworkVisibleTo`. Existing integration
   tests must pass unchanged (default relevancy = everyone visible).
6. **Per-client event filtering in `IEventBroadcaster.SendEvent`.** Small
   change.
7. **Initial-sync-on-show verification.** Integration test only.

Each step is a self-contained batch. Steps 1–4 add new behavior behind an
opt-in component; existing tests unaffected. Steps 5–6 touch hot paths —
budget time for running full integration suite.

---

## Non-MVP TODO (scaling and tuning)

Revisit these if/when they become real problems. Do not implement pre-emptively.

- **Spatial hashing for the relevancy check.** The naive per-tick loop is
  O(entities × clients). At 50 entities × 8 clients × 10 Hz = 4,000
  `IsRelevantTo` calls/sec — trivial. At 500 × 16 = 80,000 — measurable.
  Fix: bucket entities into a spatial hash (uniform grid by position),
  for each client query only entities in cells within `ShowRadius` of the
  focus. Turns the inner loop from O(entities) into O(k) where k = entities
  per relevant cell. Implementation note: this optimizes **the policy
  evaluation**, not the relevancy model. The `IRelevancyPolicy` interface
  stays the same; a `SpatialHashedDistanceRelevancy` replaces
  `DistanceRelevancy` and the `RelevancySystem` asks it for "relevant
  entities for this client" instead of iterating all.
- **Range-based fixed-point quantization.** Complement to the half-float /
  smallest-three presets that already ship. Needed when world coordinates
  exceed ~500 units and half-float error (mantissa = 10 bits) becomes
  visually noticeable. Add as a new `QuantizationMode.FixedRange` with
  configurable range/step on the attribute.
- **Cell / sector interest management.** Static world grid, entities
  subscribe to cell they occupy, clients subscribe to their cell + neighbors.
  Scales to very large worlds with many entities. Only worth it when
  distance-based relevancy stops being enough (large open-world + lots
  of static entities that rarely move).
- **Per-client prioritization / frequency scaling.** Send distant-but-
  relevant entities at a lower rate (e.g. every 3rd tick). Useful when
  relevancy radius is large intentionally (e.g. you want to see silhouettes
  of far players). Adds per-entity per-client tick counter.
- **Baseline-acked delta.** Mentioned for completeness. Not planned.

---

## Wire-format history

- **2026-04-13:** `ACS_StateBatch` per-entity records now include a
  `ushort payloadBytes` prefix between `networkObjectId` and `serverTick`.
  Enables readers to seek past unknown / despawned entities and continue
  processing the batch tail — previously a spawn-order race or late
  despawn dropped every subsequent entity in the batch (ISSUES.md #1).
  Other channels (`ACS_OwnerSubmit`, `ACS_SyncReply`, `ACS_EventBcast*`)
  are unchanged — single-entity payloads, no per-entity framing.

  This is a breaking wire change. Server and client must be rebuilt
  together (see `ISSUES.md #C1`).

---

## Open decisions locked in (from discussion 2026-04-12)

| Question | Decision |
|---|---|
| Source of client focus | First owned `NetworkObject`'s position. Pluggable via `IClientFocus`. |
| Default policy | Distance-based, hysteresis (show 50m / hide 55m as starting defaults; per-entity override via `NetworkRelevancy`). |
| Events for non-observers | Dropped silently. Global events go on singleton aspects without `NetworkRelevancy`. |
| Relevancy tick rate | ~10 Hz (every `tickRate / 10` network ticks). |
| Spatial hashing | Not in MVP. Naive loop fine for target entity counts. Revisit under load. |
