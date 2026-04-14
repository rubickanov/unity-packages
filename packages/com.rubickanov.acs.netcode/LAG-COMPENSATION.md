# Lag Compensation — Design Notes (Deferred)

Status: **not implemented, not planned for MVP.** This document captures the
reasoning so future-me can pick it up without re-deriving the tradeoffs.

Referenced from `DESIGN.md` Layer 4 (future).

---

## Decision: don't build it yet

Lag compensation solves one specific problem:

> **Server-authoritative hitscan** feels bad at high ping because the shooter
> must lead moving targets by their RTT.

All three conditions must be present for lag-comp to be worth the cost:

1. Server is the authority for hit resolution (`Authority = Server` on the
   relevant fields).
2. Hits are **hitscan** (instant) — projectiles with travel time don't need
   rewind, players naturally lead targets.
3. Gameplay is **competitive PvP** where 50–100 ms ping difference changes
   outcomes.

Remove any one condition and lag-comp becomes either unnecessary or solvable
by simpler means. None of the currently planned games under this framework
hit all three — so we skip.

### Situations where lag-comp is NOT needed

| Scenario | Why it doesn't need lag-comp |
|---|---|
| Co-op PvE | AI reaction times (200–500 ms by design) dwarf player ping. Slight hit inaccuracy is invisible. |
| Projectile PvP | Travel time forces leading anyway. Rewind adds nothing. |
| Client-authoritative PvP (`Authority = Owner`) | Shooter owns the hit decision. No server-side truth to diverge from. |
| Telegraphed abilities (ARPG zones, AoE) | Resolution window is large compared to ping; served by a plain "was target in zone at tick T" check. |
| Turn-based / slow-paced | Obviously. |

### When to reconsider

Bring this document back if any of these happen:

- A game built on this framework is a **hitscan PvP shooter** with server-auth.
- Playtests surface a recurring complaint in the shape of "shots don't register"
  or "I shot him but he killed me after going behind cover."
- Competitive scene / ranked mode requires deterministic fair hit resolution
  across ping bands.

If none of these are true, Layer 4 stays deferred indefinitely.

---

## Why premature implementation is a trap

Lag-comp is not generic infrastructure that can be written abstractly and
parked. It is a **tuned policy** against concrete variables:

- Tick rate (20/30/60 Hz changes rewind granularity)
- Max character speed (sets rewind window size)
- Hitbox topology (capsule / compound / bone tree)
- Ping profile of the target audience (region, server density)
- Game-design call: favor-the-shooter vs favor-the-victim

A framework-level scaffold without these inputs will be rewritten anyway.
The *mechanical* parts (snapshot buffer, rewind API) are cheap to add when
the real game arrives. The *policy* parts cannot be pre-decided.

---

## Infrastructure already in place

The groundwork that can be reused when we do implement:

- **`SnapshotBuffer`** (`Runtime/SnapshotBuffer.cs`) — 64-slot ring backed by a
  single `byte[Capacity * slotSize]`, `Span<byte>` slot views, zero-alloc
  after construction. Currently used owner-side for reconciliation; trivially
  symmetrized to server-side.
- **`EntityReplicator.CapturePredictedState`** — already serializes only
  `[Replicated(Predicted = true)]` fields into a caller-provided span. A
  rewind API would reintroduce the symmetric `ReadFrom → ApplyFromNetwork`
  restore path that was removed as dead code in the Batch 5 cleanup; see
  `ISSUES.md` #22 for the shape.
- **`PredictionScanner`** — produces the predicted-field list per aspect,
  including field-name map for deterministic layout.
- **`NetworkTickSystem`** — monotonic `serverTick`, needed for rewind indexing.
- **`ACS_Input`** messages already carry `clientTick` — the server knows
  "which tick the shooter perceived" without extra plumbing.

So the missing pieces are: server-side capture on every tick, a rewind API,
client-tick-to-server-tick translation accounting for interpolation delay,
and the hitbox strategy.

---

## Implementation options (when the time comes)

Four approaches, ranked by isolation from live physics:

### A. Full physics rewind (Valve/CS model)

Move `Transform`s of hitbox colliders to historical positions, call
`Physics.SyncTransforms()`, run `Physics.Raycast`, restore.

**Pros**
- Works with existing collider setup as-is.
- Any collider shape / compound / bone tree.
- Well-studied reference model.

**Cons**
- `SyncTransforms` is not free; noticeable with many entities per query.
- `OnTrigger` / `OnCollision` callbacks fire on displacement unless hitboxes
  live on a dedicated physics layer with everything else filtered out.
- Pose rewind still required for bone-mounted hitboxes (see Animation section).
- PhysX is serial — no parallel queries.

### B. Shadow hitbox pool (recommended for most cases)

Maintain a pool of GameObjects with duplicated colliders on a dedicated layer
`LagCompHitbox`. On query: position the pool to historical state, raycast
with `LayerMask = LagCompHitbox`, done. Live objects are never touched.

**Pros**
- Total isolation from live gameplay physics — zero risk of triggering game
  callbacks via displacement.
- Multiple rewind queries can run in parallel against independent pools.
- Easy to debug — pool can be visualized with Gizmos to show exactly what the
  server checked.
- No full-scene `SyncTransforms`; only the pool's layer dirties.
- Natural fit with relevancy optimization — only populate pool for nearby
  candidates.

**Cons**
- Requires an explicit "hitbox rig" description — which colliders mirror to
  the pool (typically 4–6 capsules per character: head, torso, arms, legs).
- Extra memory: `N_entities × shapes` duplicate colliders.
- Pose rewind still required if rig sits on bones.

### C. Favor-the-shooter with client hit claim

Client raycasts locally against its interpolated view, submits a
`HitClaim { targetId, shooterTick, origin, dir }`. Server rewinds **only the
claimed target** to that tick, runs a verification raycast, accepts within
tolerance (e.g. distance delta < 0.5 m).

**Pros**
- Very cheap on the server — one rewind per shot, not a broadphase.
- No need to keep server-side history for every predicted entity; can be lazy.
- Feels great for the shooter — server confirms "yes, you could have seen
  this hit from your vantage point."
- Used in some form by Valorant and Overwatch.

**Cons**
- Anti-cheat burden is higher. Client says "I hit" — server must verify:
  - Server-validated rate of fire / cooldown
  - Angular delta cap between consecutive shots
  - `origin` matches predicted shooter position at `shooterTick` within tolerance
  - Line-of-sight check (no shooting through walls)
- Two-sided logic complicates debugging.
- AoE / multi-target abilities break the "one rewind per shot" simplicity and
  collapse back to approach B.

### D. Hybrid: analytical broadphase + physics precise

Maintain a per-entity capsule envelope (root position + radius covering pose
space). Broadphase = pure capsule-vs-ray math. Survivors get a precise physics
rewind via A or B.

**Pros**
- Broadphase trivially scales to hundreds of entities.
- Physics rewind only runs for 0–2 candidates; great amortization.

**Cons**
- Two subsystems instead of one. Not much extra code, but two points of bugs.

---

## Animation handling

This is where "do it properly" escalates from days to weeks. Three tiers:

### Tier 1: root-transform rewind only

Rewind `Position` and `Rotation` of the entity root. Hitboxes on bones follow
whatever pose the renderer happens to be in. A player crouching on their screen
while standing on the server will not register head-shots correctly.

- **Cost:** zero. `[Replicated(Predicted = true)]` already gives us this.
- **Suffices for:** PvE, co-op, slow PvP, generous hitboxes.

### Tier 2: pose envelope (forgiveness capsule)

Server-side hitbox is a single fat capsule sized to cover all possible poses
of the character. No pose rewind needed; no locational damage.

- **Cost:** zero. Different collider setup, that's all.
- **Suffices for:** shooters without headshots, MOBA-like gameplay.

### Tier 3: full bone rewind

Snapshot `Transform[]` of every bone each tick. Apply on query.

- Memory: ~60 bones × ~40 bytes × 64 ticks × N entities. Order of 10 MB for
  16 entities — not catastrophic but not free.
- CPU capture cost per tick per entity is non-trivial.
- On rewind, either apply to the shadow pool (B) or sample an Animator (A).

**The hard sub-problem:** headless dedicated servers typically don't run
Animators. To know the pose on the server, one of:

- Evaluate Animator server-side (CPU-heavy, defeats the point of headless).
- Replicate pose from owner (bandwidth-heavy, scales poorly).
- Derive pose deterministically from replicated state (speed, stance,
  cycle phase) so both client and server arrive at the same pose. This is
  what CS:GO effectively does in simplified form. The client Animator renders
  the same pose plus cosmetic layers that do not affect hitboxes.

Tier 3 with deterministic pose is a multi-week project on its own, orthogonal
to the rewind mechanism itself.

---

## Recommended first implementation

When the trigger fires (see "When to reconsider"), start with:

**Approach B (Shadow hitbox pool) + Tier 2 (pose envelope)**

- 3–5 days of focused work.
- Leaves live physics untouched.
- Fits the existing `SnapshotBuffer` primitive — add a server-side history
  buffer symmetric to the owner one, and wire it to a `Rewind(int tick)`
  scope on `EntityReplicator`.
- Anti-cheat cap: clamp rewind to `[serverTick - maxRewind, serverTick]` with
  `maxRewind` around 200 ms. One-line guard, big impact.

Upgrade path:

- Add **D** (analytical broadphase) when profiler shows rewind as a hotspot
  with many entities on the map.
- Add **Tier 3** (bone rewind with deterministic pose) only if playtest
  feedback specifically calls out "headshots feel wrong at high ping."

Approaches to **avoid** in the first iteration:

- **A** (full rewind against live physics) — easy to introduce collision
  regressions in game-critical triggers.
- **Tier 3 with server-side Animator** — overkill for anything short of
  a ranked competitive shooter.

---

## Key tuning variables to decide per-game

When the real game arrives, these must be pinned down before writing code:

| Variable | Typical range | Notes |
|---|---|---|
| Max rewind window | 100–250 ms | Trades favor-shooter feel vs "killed behind cover" frustration |
| Server history size | `maxRewind / tickInterval` slots | Directly sized from rewind window |
| Interpolation compensation | `2 * tickInterval` (default) | Shooter sees others delayed by this; rewind must subtract it |
| Hitbox strategy | Tier 1 / 2 / 3 | See animation section |
| Rate-of-fire cap (server) | per-weapon | Only matters under approach C |
| Angular delta cap (server) | per-weapon | Only matters under approach C |
| Favor-the-shooter vs victim | design call | Not a tuning knob after the fact — pick early |

The "shooter's perceived tick → server rewind tick" mapping is the single
most common place to get lag-comp wrong. The shooter sees **their own**
character predicted (current tick) but **other** characters interpolated
(current tick minus interpolation delay). Rewind of targets therefore uses
`shooterTick - interpolationDelayTicks`, not `shooterTick`. Forgetting this
causes systematic "shots behind the target" errors.

---

## Summary

- Not needed for PvE, co-op, projectile PvP, client-auth PvP, telegraphed-AoE,
  or slow-paced games — i.e. almost certainly not needed yet.
- The framework already holds the cheap bits (`SnapshotBuffer`, predicted
  field capture, tick-tagged inputs). The expensive bits (hitbox strategy,
  pose model, anti-cheat cap tuning) depend on a concrete game and cannot
  be committed ahead of time.
- When the trigger fires: shadow hitbox pool + pose envelope + 200 ms cap.
  Iterate from there.
