# ACS Netcode — Known Issues

Audit scope: every `.cs` file under `Runtime/` (Editor code and tests excluded).
Date: 2026-04-12.

Severity scale:
- **MAJOR** — actively breaks or can corrupt state / loses data / causes allocation storms on hot paths / fails on supported platforms.
- **MINOR** — smell, wasted allocation off the hot path, fragile invariant, docs/code drift, dead code.

Each issue lists file:line, what is wrong, and a concrete fix.

---

## MAJOR

### #1 — `OnStateBatchReceived` drops the remainder of the batch on unknown entity

**File:** `Runtime/AspectReplicationSystem.cs:317–341`

When a `networkObjectId` in the incoming `ACS_StateBatch` is not found in
`_byNetworkObjectId` (late spawn on sender / early despawn on receiver /
ordering race between `NetworkObject.Spawn` on server vs client), the handler
logs a warning and `return`s. The reader cannot seek past the unknown record
because the wire format only encodes the layout (mask size, per-field sizes)
through the replicator instance we do not have. So **every subsequent entity
in the same batch is silently lost**.

This is acknowledged in the code comment ("Remaining batch entries lost") —
a known hole, not a fresh bug.

**Fix:** prefix every per-entity record in `ACS_StateBatch` with a
`ushort payloadBytes` length. On unknown id, reader can `Seek(Position +
payloadBytes)` and continue. Wire change — bump format version and update
both `ServerTick` writer and the receive handler.

---

### #2 — `PredictionHookCache` allocates `object[]` on every state batch

**File:** `Runtime/AspectReplicator.cs:864–890`

`Register` / `Unregister` / `Reconcile` closures each build
`new object?[] { nm, null }` and `new object[] { rep, serverTick }` on
every invocation and pass them through `MethodInfo.Invoke`. `Reconcile` is
called from `AspectReplicationSystem.OnStateBatchReceived` for every
predicted entity on every received state batch — i.e. `tickRate × N_entities`
times per second. This is the same hot path the rest of the netcode layer
already optimized away (`CustomMessagingManager` instead of per-tick byte[]
RPCs, etc.).

**Fix:** store the typed `PredictionManager<TInput>` reference once at
`Register` time (cache by `(Type, NetworkManager)`) and invoke
`OnServerStateApplied` / `Unregister` through a typed
`Action<AspectReplicator, int>` / `Action<AspectReplicator>` delegate.
No `MethodInfo.Invoke`, no object[] per call.

---

### #3 — 256-binding cap silently drops bindings and continues spawn

**Files:**
- `Runtime/AspectReplicator.cs:245–250` (fields)
- `Runtime/AspectReplicator.cs:324–331` (events)

When an entity exceeds 256 replicated fields or events, the code logs an
error and `Array.Resize`s the tail away — spawn proceeds with a truncated
binding list. If different peers hit the limit differently (different logs /
mods / versions), their bitmask positions diverge and **incoming payloads
get written to the wrong fields** on the receiver.

The cap itself is legitimate (event index is packed into a byte; mask bits
are per-binding) but silent truncation is strictly worse than failing spawn.

**Fix:** on overflow, abort spawn — log a fatal error and return from
`OnNetworkSpawn` without registering with the replication system.
Alternatively throw. No truncation.

---

### #4 — Post-clamp invariants in `OnNetworkSpawn` are not asserted

**File:** `Runtime/AspectReplicator.cs:245–296`

Order of clamps / recomputation in `OnNetworkSpawn`:
1. `_bindings.Length > 256` → `Array.Resize` both `_bindings` and
   `_bindingAuthorities` (line 248–249).
2. `_maskByteCount` recomputed from the clamped `_bindings.Length`
   (line 254).
3. `_statePayloadCap` computed from clamped `_bindings` (line 256–259).
4. `EnforceEventBindingCap(ref _eventBindings, ...)` clamps events
   (line 275).
5. `_predictedBindingIndices` is **filtered** against the clamped
   `bindingLimit` (line 284–291).

Three array resizes plus a filter, with no ordering check. Any reshuffle
could quietly break the invariant `_bindings.Length == _bindingAuthorities.Length`
or misalign predicted indices with bindings.

**Fix:** at end of `OnNetworkSpawn`, assert:

```csharp
Debug.Assert(_bindings.Length == _bindingAuthorities.Length);
Debug.Assert(_maskByteCount == (_bindings.Length + 7) / 8);
Debug.Assert(_dirtyMaskBuffer.Length == _maskByteCount);
foreach (var idx in _predictedBindingIndices)
    Debug.Assert(idx < _bindings.Length);
```

---

### #5 — `Expression.Lambda.Compile()` in binding factories risks IL2CPP breakage

**Files:**
- `Runtime/ReplicatedFieldBinding.cs:177–213`
  (`BuildFieldFactory` / `BuildInterpFactory` / `BuildAuthorityRenderFactory`)
- `Runtime/ReplicatedEventBinding.cs:99–114` (`BuildFactory`)

Both factories build per-`valueType` delegates via
`Expression.Lambda<...>.Compile()`. On IL2CPP, `Expression.Compile`
either routes through the interpreter (slow, per-call allocations) or
throws `PlatformNotSupportedException` depending on Unity version and
`System.Linq.Expressions` availability. There is no fallback.

**Fix:** replace with `Activator.CreateInstance(bindingType, args)` (slower
but works on IL2CPP) or a hand-written switch-ladder by `valueType` for the
known unmanaged types — mirroring `Interpolators.Lerpers`. The set of
supported types is small and bounded, so the ladder is short. Either way,
verify on an IL2CPP build before claiming IL2CPP support in the README.

---

### #6 — `AotHints` is missing `AuthorityRenderBinding<T>`

**File:** `Runtime/AotHints.cs:26–58`

`AotHints.UsedOnlyForAOTCodeGeneration` instantiates
`ReplicatedFieldBinding<T>`, `InterpolatedFieldBinding<T>`,
`ReplicatedEventBinding<T>`, and `ReactivePropertyExtensions.Smooth<T>`
for common unmanaged types. **`AuthorityRenderBinding<T>` is not there.**

`AuthorityRenderBinding<T>` is created by
`ReplicatedFieldBindingFactory.BuildAuthorityRenderFactory`
(`ReplicatedFieldBinding.cs:194–198`) via `MakeGenericType` —
exactly the path IL2CPP cannot discover statically. First spawn of a
predicted player with a smoothed `[Replicated(Interpolation = Linear,
Predicted = true)] Vector3 Position` will throw `ExecutionEngineException`.

**Fix:** add the same seven instantiations as `InterpolatedFieldBinding<T>`:

```csharp
new AuthorityRenderBinding<float>(default!, default!);
new AuthorityRenderBinding<double>(default!, default!);
new AuthorityRenderBinding<Vector2>(default!, default!);
new AuthorityRenderBinding<Vector3>(default!, default!);
new AuthorityRenderBinding<Vector4>(default!, default!);
new AuthorityRenderBinding<Quaternion>(default!, default!);
new AuthorityRenderBinding<Color>(default!, default!);
```

---

## MINOR — dirty code / smells / perf nits

### #7 — `HandleOwnerEvent` copies event payload three times

**File:** `Runtime/AspectReplicator.cs:700–742`

Flow: read into `stackalloc byte[payloadSize]` → write to `relayWriter`
(copy #1) for broadcast → write to a second `localWriter` (copy #2) →
construct `localReader` from `localWriter` → `binding.ApplyFromNetwork(localReader)`
which reads bytes back (copy #3).

Copies #2 and #3 exist only because `ApplyFromNetwork` takes a
`FastBufferReader`. We already have the bytes in `temp`; construct a
`FastBufferReader` over `temp` directly.

**Fix:** skip the `localWriter` round-trip, build the reader from the
stackalloc pointer (or re-use `relayWriter` as the source reader since its
bytes are still valid until `Dispose`).

---

### #8 — `ReplicationScanner.ScanEvents` silently drops non-`Subject<T>` fields

**File:** `Runtime/ReplicationScanner.cs:163–170`

When a field has `[ReplicatedEvent]` but its type is not `Subject<T>`,
the scanner `continue`s without logging. The sister path for `[Replicated]`
fields logs `Debug.LogError` at line 108. A developer who accidentally
puts `[ReplicatedEvent]` on a `ReactiveProperty<T>` (or a plain field) will
silently lose event wiring.

**Fix:** add a symmetric `Debug.LogError` with the same "field X has
[ReplicatedEvent] but is not a Subject<T>" message.

---

### #9 — `ServerTick` scans the dirty mask twice

**File:** `Runtime/AspectReplicationSystem.cs:163–248`

Pass 1 (lines 166–198) builds the per-entity mask and computes
`dirtyCount` + `totalPayloadSize`. Pass 2 (lines 210–240) walks the
mask buffer again (lines 220–225) just to recompute `anyDirty`, which was
already known in pass 1. `O(replicators × maskBytes)` of wasted work per
tick.

**Fix:** during pass 1, append each dirty replicator's index to a
scratch `List<int>` (field, reused per tick). Pass 2 iterates the list.
No redundant mask scan.

---

### #10 — Per-spawn allocation cloud in `OnNetworkSpawn`

**File:** `Runtime/AspectReplicator.cs:107–238`

For every entity spawn the method allocates:
- `new List<ReplicatedFieldBinding>()` (all bindings)
- `new List<AuthorityMode>()` (all authorities)
- `new List<ReplicatedFieldBinding>()` (interpolated subset)
- `new List<ReplicatedEventBinding>()` (events)
- `new List<PredictedFieldInfo>()` (predicted)
- `new List<int>()` (predicted indices)
- `new List<object>()` (aspects, sorted)
- `new HashSet<string>()` **per aspect** with predicted fields
- `new Dictionary<string, int>()` **per aspect** (binding-by-name map)
- `.ToArray()` on 4–6 of the lists

A realistic entity with 5 aspects → ~15+ short-lived allocations on one
spawn. Spawning a wave at once is GC-visible.

**Fix:** reuse fields — `_scopeComponentsBuffer` on line 41 already does
this for scope walking. Same pattern for all the scratch lists and the
per-aspect `Dictionary<string, int>` (clear, not re-allocate). Count-first
pass + direct array writes eliminates the `.ToArray()` copies.

---

### #11 — `ApplyStateBuffer(byte[], StateApplyMode)` test shim in production class

**File:** `Runtime/AspectReplicator.cs:507–522`

Comment is honest: *"Shim for existing unit tests that pass byte[]
payloads and do not need the server tick."* A test-only API living in the
runtime class.

**Fix:** migrate the tests to `FastBufferReader` (assembly already has
`InternalsVisibleTo("ACS.Runtime.Netcode.Tests")`) or move the shim into
a test-only internal helper. Production class should carry the
`FastBufferReader` overload only.

---

### #12 — `ResolveInputType` is reflection-heavy on every spawn

**File:** `Runtime/AspectReplicator.cs:788–807`

Per spawn:
1. `GetComponentsInChildren<MonoBehaviour>(includeInactive: true)` —
   every MonoBehaviour under the entity (often dozens).
2. For each: `GetComponentInParent<NetworkObject>()` — a second hierarchy
   traversal.
3. For each: `GetType().GetInterfaces()` — reflection, allocates an array.
4. For each interface: `IsGenericType` + `GetGenericTypeDefinition()`.

For a prefab with 30 MonoBehaviours: 30 `GetComponentInParent` walks, 30
`GetInterfaces()` reflection calls, plus the interface enum per component.

**Fix:** cache `Type → TInput?` keyed by the first-component's type
(spawns of the same prefab repeat the same lookup). Replace
`GetComponentInParent<NetworkObject>()` with `Transform.IsChildOf` against
the already-cached `NetworkObject.transform`.

---

### #13 — `InterpolatedFieldBinding.TickRender` uses linear ring-buffer scan

**File:** `Runtime/InterpolatedFieldBinding.cs:89–102`

Every frame, `TickRender` walks `newest → oldest` linearly looking for the
first sample whose time `≤ renderTime`. Average 2–3 iterations at steady
state (render delay = 2 ticks, buffer = 32), but up to 32 under packet
jitter. Multiplied by `_interpolatedBindings.Length × framerate`.

**Fix:** `renderTime` is monotonically increasing — store a
`_lastLowerIdx` and start the search from there. Amortized O(1) per frame,
bounded catch-up on delay spikes.

---

### #14 — `SnapshotBuffer` allocates 64 separate `byte[]`s per owner spawn

**File:** `Runtime/SnapshotBuffer.cs:41–47`

```csharp
_slots = new Slot[Capacity];
for (int i = 0; i < Capacity; i++)
    _slots[i].Data = new byte[slotSize];
```

64 small array allocations at spawn for each predicted entity.

**Fix:** one `byte[Capacity * slotSize]` + index arithmetic, or
`Span<byte>` slices. Single allocation, same indexing logic.

---

### #15 — `_ownerSubmitTickOffset` fixed on first submission only

**File:** `Runtime/AspectReplicator.cs:527–533`

```csharp
if (_ownerSubmitTickOffset == int.MinValue)
    _ownerSubmitTickOffset = serverTick - senderTick;
```

NGO re-syncs client clocks throughout the session. The offset computed on
the very first submission is used forever afterwards. Over a long session
with real drift, `receivedTime` decouples from actual server time. Not
visible immediately but drifts interpolation timestamps for owner-auth
fields.

**Fix:** recompute on every submission with EMA smoothing
(e.g. `offset = 0.9 * offset + 0.1 * (serverTick - senderTick)`).
Or, if exact offset is needed, recompute each time. Ownership transfer
already resets it (line 372) so the reset path is clean.

---

### #16 — `PredictionManager<TInput>._tickDelta = 0` silently breaks Simulate

**File:** `Runtime/PredictionManager.cs:75–76`

```csharp
uint tickRate = networkManager.NetworkTickSystem.TickRate;
_tickDelta = tickRate > 0 ? 1f / tickRate : 0f;
```

If `tickRate == 0`, every `Simulate(in input, 0f)` call is effectively a
no-op — motion freezes, and there is no warning. `AspectReplicator` bails
interpolation at the same check (lines 262–273) but `PredictionManager`
proceeds silently.

**Fix:** either early-return from `OnTick` when `_tickDelta == 0` with a
one-time warning, or refuse to build the manager in the first place
(throw or log and return null from `GetOrCreate`).

---

### #17 — Static caches survive Play Mode sessions without Domain Reload

**Files (all):**
- `Runtime/AspectReplicationSystem.cs:27` (`s_Systems`)
- `Runtime/PredictionManager.cs:43` (`s_Systems`)
- `Runtime/ReplicationScanner.cs:46–48`
  (`StateCache` / `EventCache` / `UnmanagedCache`)
- `Runtime/ReplicatedFieldBinding.cs:133–136` (3× `Factories`)
- `Runtime/ReplicatedEventBinding.cs:86` (`Factories`)
- `Runtime/AspectReplicator.cs:823` (`PredictionHookCache.s_Cache`)
- `Runtime/InterpolationRegistry.cs:25` (`Bindings`)
- `Runtime/NetworkScopeScanner.cs:13` (`Cache`)
- `Runtime/PredictionScanner.cs:28` (`Cache`)

With *Enter Play Mode → Disable Domain Reload* (a common dev-loop speedup),
every static dictionary in this list survives stop/start. Old
`NetworkManager` entries stay in `s_Systems` until the next domain reload.
Not a runtime leak in production (fresh `NetworkManager` each session
means new keys), but tests and editor-only repeated play/stop cycles
accumulate stale entries and can mask bugs.

**Fix:** in every class that holds static state, add:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStatics()
{
    s_Systems.Clear(); // or whichever field
}
```

---

### #18 — `_statePayloadCap` is misnamed, used in exactly one site

**Files:** `Runtime/AspectReplicator.cs:55, 256–259` +
`Runtime/AspectReplicationSystem.cs:386`

Field claims to be a "fixed-capacity payload size for state messages" but
`ServerTick` recomputes the actual payload size per tick from the current
dirty mask (lines 166–198). The only consumer is
`OnSyncRequestReceived` (line 386), which uses it as a
`FastBufferWriter` pre-size hint for the initial-sync payload.

**Fix:** rename to `_initialSyncPayloadHint` or compute inline at the call
site. As-is it reads like a runtime invariant that matters more broadly
than it actually does.

---

### #19 — Two parallel caches: `ReplicationScanner.StateCache` and `PredictionScanner.Cache`

**Files:**
- `Runtime/ReplicationScanner.cs:46`
- `Runtime/PredictionScanner.cs:28`

After the `[Replicated(Predicted = true)]` unification,
`ReplicatedFieldInfo` already carries `bool Predicted`
(`ReplicationScanner.cs:16`). `PredictionScanner.Scan` is documented as a
"thin filter over ReplicationScanner", but it still maintains its own
`Dictionary<Type, PredictedFieldInfo[]>` — a second cache doing the same
work.

**Fix:** delete `PredictionScanner.Cache`. `Scan` becomes:

```csharp
public static PredictedFieldInfo[] Scan(object aspect)
{
    var replicated = ReplicationScanner.Scan(aspect);
    // count → allocate once → copy
}
```

Result is still an array (needed for stable index access downstream), but
only `ReplicationScanner.StateCache` is authoritative. Bonus: one fewer
per-type dictionary.

---

### #20 — `InterpolationRegistry.Bindings` is `Dictionary<object, object>`

**File:** `Runtime/InterpolationRegistry.cs:25`

Untyped map — every caller stores `ReactiveProperty<T>` as key and
`IInterpolatedBinding<T>` as value through the `object` surface.
`TryGetInterpolatedValue<T>` does an `is IInterpolatedBinding<T>` cast to
recover types. Works today because both consumer sites
(`InterpolatedFieldBinding`, `AuthorityRenderBinding`) ctor-register and
`OnDespawn`-unregister symmetrically.

Risks:
- No guard against double-register on the same `ReactiveProperty` (would
  silently overwrite the prior binding).
- Any non-matching `T` at read time silently returns false.
- Ownership transfer paths could, in principle, leak if
  `OnLostOwnership` forgot to unregister — currently it doesn't trigger
  despawn, but the binding type is also fixed at spawn so this is not an
  active bug.

**Fix (optional):** key by `(ReactiveProperty, T)` implicitly via a
typed dictionary per closed generic
(`static class InterpolationRegistry<T> { static Dictionary<ReactiveProperty<T>, IInterpolatedBinding<T>> Map; }`).
Or keep the current map but assert in `Register` that the key is not
already present (or choose overwrite semantics explicitly and document).

---

### #21 — Interpolation render delay is hardcoded at 2 ticks

**File:** `Runtime/AspectReplicator.cs:266`

```csharp
_interpolationDelaySeconds = 2.0 * _tickInterval;
```

Fine for typical Unity games; fast-paced games (shooters at 60Hz+) may want
less. No way to configure it without forking.

**Fix:** expose as a property on `AspectReplicator` (default 2), or attach
a per-entity `[NetworkInterpolationDelay(ticks)]` attribute. Non-urgent —
document the default first and move on.

---

### #22 — `AspectReplicator.RestorePredictedState` is dead code

**File:** `Runtime/AspectReplicator.cs:622–640`

Method is defined and wired through the same
`ReadFrom → ApplyFromNetwork → WriteSuppressed` path the network-receive
code uses, but has no callers anywhere in the package. `PredictionManager
<TInput>.OnServerStateApplied` intentionally replays inputs on top of the
authoritative state that `ApplyStateBuffer` just wrote, without restoring
any prior snapshot — so `RestorePredictedState` sits unused.

This is a side effect of the always-replay reconcile strategy landed in
step 7 (batch 3.8). The method was scaffolded for a predict-threshold
rewind path ("only replay when `|predicted - authoritative| > ε`") that
was never implemented.

**Fix:** delete `RestorePredictedState` + `_predictedPayloadSize` reader
references. If/when a predict-threshold path is added, bring it back
alongside the caller. Leaving it in place now misleads readers of
`AspectReplicator` about what the reconcile path actually does.

---

### #23 — `AuthorityRenderBinding` timing constants assume a ~30 Hz tick rate

**File:** `Runtime/AuthorityRenderBinding.cs:49, 62`

```csharp
private const double CoalesceWindowSeconds = 0.010;
private const double StaleSampleThresholdSeconds = 0.066;
```

Both magic numbers are sized around a 30 Hz tick rate (33 ms interval):
- `CoalesceWindowSeconds = 10 ms` — "well below any realistic tick interval (≥30 Hz = 33 ms)". At tick rates ≥ 100 Hz the interval drops below 10 ms and legitimate consecutive ticks get coalesced into the same `_curr`, which is exactly the "collapse to same wall-clock instant" failure mode the coalescing was meant to prevent.
- `StaleSampleThresholdSeconds = 66 ms` — "~2 ticks @ 30 Hz". At 15 Hz one tick is already 66 ms, so every normal tick is at the threshold — any jitter pushes `gap > threshold` and `RecordSample` takes the bootstrap branch, dropping `_hasPrev`. Render then stalls on `_curr` every tick instead of smoothing.

The thresholds implement real invariants (intra-frame coalescing, stale-sample bootstrap) but they need to track `NetworkTickSystem.TickRate`, not wall-clock constants.

**Fix:** compute both windows from `_tickDelta` at binding construction time, e.g.
`CoalesceWindow = 0.3 * tickDelta`, `StaleThreshold = 2.5 * tickDelta`. Plumb
`tickDelta` in through the factory (same path `InterpolatedFieldBinding` already
uses for its lerper). Mid-session tick-rate changes aren't a thing in NGO, so a
spawn-time capture is sufficient.

---

## Verified false alarms (exploration agents flagged these; none are real)

- **"Race condition in `OnTick` / `ServerTick` / broadcast-target rebuild."**
  All of these run on `NetworkTickSystem.Tick`, which is single-threaded.
  No races.

- **"Off-by-one in dirty-mask bit indexing."** Little-endian layout
  (`i & 7`) is symmetric between `ServerTick`/`OwnerTick` writer and
  `ApplyStateBuffer`/`ApplyOwnerSubmission` reader. Correct.

- **"Null deref in `PredictionManager.OnInputReceived` unsafe read."**
  `FastBufferReader.ReadBytesSafe` throws when the buffer doesn't have
  enough bytes. That's the whole point of the "Safe" suffix. Not a null
  deref path.

- **"Missing null-check on `_broadcaster` in `ReplicatedEventBinding.OnLocalEvent`."**
  `_broadcaster` is only read after `SubscribeAsAuthority` populates it.
  Subscription is the sole entry point to `OnLocalEvent`.

- **"`_pendingValue` overwrite / lost-write race in `ReplicatedFieldBinding`."**
  Protocol is strictly `ReadFrom → ApplyFromNetwork` sequential on a
  single-threaded tick handler. `_hasPendingValue` clears correctly.

- **"Subscription-gate race in `EntityNetworkComponent.TrySubscribe`."**
  `AspectReplicator.OnNetworkSpawn` calls `ApplyNetworkScopes` *before*
  registering itself, and `Behaviour.enabled = false` synchronously fires
  `OnDisable → TryDispose`. The `!enabled` check at
  `EntityNetworkComponent.cs:58` is what closes the ordering hole
  (regression #16 per the in-code comment).

- **"Silent validation failure in `ReplicationScanner.Scan`."**
  The field path at `ReplicationScanner.cs:108` does `Debug.LogError`. The
  bug is in the *event* path (`#8` above), not the state path.

---

## Fix priority

1. **#1** — data loss on unknown-entity batches (wire-format change).
2. **#2** — per-batch `object[]` allocations on the reconcile hot path.
3. **#3** — silent binding truncation → peer-level bitmask divergence.
4. **#5 / #6** — IL2CPP viability (`Expression.Compile` + AOT hints).
   Must verify on an IL2CPP build before claiming IL2CPP support.
5. Everything else is cleanup / perf polish — batch into a single sweep.
