# Delta Compression + Relevancy — Design & Plan

Status: **planned, not implemented.** Referenced from `DESIGN.md` Layer 5.

This document locks in the scope and design decisions so implementation can
proceed in a single focused pass.

---

## Context: what's already in place

`AspectReplicationSystem.ServerTick` already does **field-level dirty delta**:
each entity carries a `dirtyMask` bitset, and only dirty bindings are written
to the batch. The wire format documented in `DESIGN.md` (Layer 0) is
already delta-compressed at the field granularity.

What's **not** in place:

- **Per-client filtering.** A single `ACS_StateBatch` is sent to every
  non-host client via `_broadcastTargetIds`. Irrelevant entities ride along.
- **Entity-level subscription.** There's no concept of "client C does not
  need entity E at all."
- **Value-level compression.** Each binding writes raw `sizeof(T)` bytes —
  no quantization, no bit-packing.

Relevancy closes the first two gaps. Quantization is a separate, optional
feature (see "Out of scope" below).

---

## Scope decision

**In scope for this iteration:**

- Per-entity relevancy policy (distance-based default).
- Per-client state batching in `AspectReplicationSystem`.
- Per-client event filtering.
- Integration with NGO's `NetworkObject.Observers` / `NetworkShow` / `NetworkHide`
  so spawn/despawn and initial sync flow through the existing pipeline.

**Out of scope (deferred, revisit only if profiler demands):**

- Value-level quantization (`int16` positions, smallest-three quaternions).
  Add as an opt-in attribute knob after relevancy is in place and bandwidth
  is re-measured.
- Baseline-acked delta (Quake-3 snapshot model). Large complexity, small
  incremental win after field-dirty + relevancy. Not planned.
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
`AspectReplicationSystem` / `PredictionManager`. Lookup resolves on first
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

Per-`NetworkManager` singleton, mirrors `AspectReplicationSystem` lifecycle:

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

`AspectReplicationSystem.ServerTick` restructures from "one batch for all"
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
2. Client's `AspectReplicator.OnNetworkSpawn` fires.
3. It calls `AspectReplicationSystem.RequestInitialSync(this)` → sends
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
- **Value-level quantization.** Add `[Replicated(Quantize = ...)]` knobs
  for positions / rotations. Only justified if profiler shows float-dominated
  payloads after relevancy is in place. Squad-style int16 ±500m / 0.015m
  step gives 50% savings on Vector3; smallest-three gives 75% on quaternions.
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

## Open decisions locked in (from discussion 2026-04-12)

| Question | Decision |
|---|---|
| Source of client focus | First owned `NetworkObject`'s position. Pluggable via `IClientFocus`. |
| Default policy | Distance-based, hysteresis (show 50m / hide 55m as starting defaults; per-entity override via `NetworkRelevancy`). |
| Events for non-observers | Dropped silently. Global events go on singleton aspects without `NetworkRelevancy`. |
| Relevancy tick rate | ~10 Hz (every `tickRate / 10` network ticks). |
| Spatial hashing | Not in MVP. Naive loop fine for target entity counts. Revisit under load. |
