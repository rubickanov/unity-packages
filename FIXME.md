# Fix priorities

Cross-package order for working through the audit backlog. Per-package detail lives in each
`packages/com.rubickanov.<name>/issues.md` — this file is the single source of truth for
**what to fix first**. Fix top-down; check items off as they land.

Tiers: **P1** core-correctness crashes in pure-C# foundations everything depends on →
**P2** runtime/netcode correctness (data loss, leaks, NRE) → **P3** UI / tools / security
(fix as those packages get used) → **P4** README drift (must clear before `docs/generate.sh`)
→ **P5** low / cosmetic / known-limitation (fix opportunistically or leave documented).

Counts from the audit: **14 critical + 37 minor** across 22 packages. (`ui.localization`'s
original "critical" was a verified false positive — see its `issues.md`.)

---

## P1 — Core correctness (pure-C# foundations, fix first)

These are crashes/wedges in packages the rest of the stack builds on. A bug here poisons
every consumer.

- [x] **statemachine** — *(critical)* FSM permanently wedges if `OnEnter`/`OnExit` (or
  `OnEnterAsync`/`OnExitAsync`) throws or is cancelled: `_isTransitioning`/`_transitionDepth`
  never reset, so all later `SetState` calls silently queue forever. Cancellation is a
  documented feature, so this is reachable normally.
  `Runtime/StateMachine.cs:153-192` & `:87-109`; `Runtime.Async/AsyncStateMachine.cs:162-205`
  & `:90-115`. Fix: wrap transition body + initial enter in `try/finally` that resets state.
  Fixed: transition body + initial enter now reset `_isTransitioning`/`_transitionDepth`/pending
  on exception escape (sync + async); regression tests cover recover-after-throw/cancel.
- [x] **gas** — *(critical)* Reentrant `ApplyEffect`/`RemoveEffect`/`RemoveEffectsWithTag`
  from a `GameplayAttribute.ValueChanged` handler throws `InvalidOperationException` (shared
  `_dirtyAttributes` cleared mid-`foreach`). Realistic "shield drops → apply counter-effect"
  pattern. `Runtime/Effects/EffectController.cs:302` + `:12`. Fix: snapshot dirty tags into a
  per-call buffer before iterating.
  Fixed: `RecalculateAttributes` snapshots dirty tags into a stack buffer before iterating;
  also moved the removed-effects snapshot (`DetachRemoved`) ahead of recalc so the no-longer-
  crashing path can't silently drop `EffectRemoved` when reentrancy reuses `_pendingRemoved`.
  Regression tests cover apply/remove-from-`ValueChanged` and removed-event survival.
  - [x] *(minor, do together)* `EffectController` never unsubscribes from
    `AttributeSet.BaseValueChanged` → stale-controller leak. `:36`. Add `IDisposable`/`Detach`.
    Fixed: `EffectController` is now `IDisposable`; `Dispose()` detaches the handler (idempotent).
- [x] **gameplaytags** — *(crash, filed minor)* `Matches` guards only the lower index bound;
  a stale/out-of-range tag index throws `IndexOutOfRangeException` through the whole public
  surface (`Matches`/`HasTag`/`HasAll`/`HasAny`). `Runtime/GameplayTagRegistry.cs:210-221`.
  Fix: add upper-bound guard (`tag.Index >= _parents.Count` → return false).
  Fixed: added upper-bound guard on both operands; regression test covers stale out-of-range tag.
- [x] **utils** — *(crash, filed minor)* `EvictingPool` doesn't validate `maxActive`; with
  `maxActive <= 0` the first `Get()` NREs in `EvictOldest()` instead of failing clearly at
  construction. `Runtime/Unity/EvictingPool.cs:45-55`. Fix: throw
  `ArgumentOutOfRangeException` in ctor (mirror sibling `ObjectPool`).
  Fixed: ctor now guards non-positive `maxActive` and negative `evictBuffer` (mirrors
  `ObjectPool`); regression tests cover both.

## P2 — Runtime / netcode correctness (data loss, leaks, NRE)

- [x] **acs.netcode** — *(critical)* Server-auth `[ReplicatedEvent]` double-subscribes on a
  host across an ownership re-gain (server hands ownership to a client, then takes it back —
  vehicles/possession). Each regain adds another subscription → every event fires N+1 times
  (double-applied for `Reliable`). `Runtime/Replication/EntityReplicator.cs:385` +
  `EntityReplicator.Events.cs:92`. Fix: on ownership transfer subscribe only owner-auth
  bindings (skip `Authority == Server`); leave spawn-time server-auth subs untouched.
  Fixed: new `SubscribeOwnerEventBindings()` (owner-auth only → `_ownerDisposables`);
  `OnGainedOwnership` calls it instead of the shared spawn-time helper. Regression test
  covers the host -> client -> host round-trip.
- [x] **acs.persistence** — *(data loss, filed minor)* Collection bindings `Clear()` before
  the type cast, so restoring a type-mismatched (non-null) snapshot leaves the live collection
  empty. `PersistedListBinding.cs:29`, `PersistedHashSetBinding.cs:27`,
  `PersistedDictionaryBinding.cs:27`. Fix: cast (or copy source) before `Clear()`.
  Fixed: all three cast to the target `IEnumerable` before `Clear()` (null still clears);
  regression tests cover each collection type.
- [x] **config** — *(critical)* Addressables handle leaked on cancel/failure: `LoadAsync`
  returns/throws before handing the handle back as a release token, so faulted/cancelled
  handles are never released. `Runtime/AddressablesAssetLoader.cs:14-20`. Fix: try/catch +
  `Addressables.Release(handle)` on the failure path before rethrowing.
  Fixed: `LoadAsync` releases the handle (guarded by `IsValid()`) on any exception before
  rethrowing. No unit test — path needs the live Addressables runtime.
- [x] **audio** — *(critical)* `RentSource` NREs when every source is in fade-out limbo
  (pool empty *and* `_activeSources` empty → `_activeSources.First` is null). Reachable with
  enough concurrent fade-outs. `Runtime/UnityAudioService.cs:109-131`. Fix: guard empty active
  list (fresh source or return `SoundHandle.Invalid`).
  Fixed: `RentSource` grows a fresh source when both pool and active list are empty (explicit
  `_activeSources.Count > 0` eviction branch). Regression test added.
  - [x] *(minor)* SFX fade-in uses service-lifetime token, can write volume to a recycled
    source. `:257-289`. Tie fade to the per-source watcher token.
    Fixed: `BeginWatch` now runs before `StartPlayWithFade`; the watcher token is threaded into
    `FadeInAsync`, so `EndWatch` cancels the fade when the source is recycled.
  - [x] *(minor)* `package.json` declares Unity 2022.3 but `AudioResource` API needs 2023.2+.
    Bump declared minimum.
    Already resolved: `package.json` declares `"unity": "6000.0"`.

## P3 — UI / tools / security (fix as these get used)

- [x] **ui** — *(2 critical)* `AddTooltip(..., delay: 0f)` and `AttachPopup(..., delay: 0f)`
  are silent no-ops: `_cancelled` set true in `CancelScheduledShow()` and never reset on the
  immediate branch, so `Show*` early-returns. `UIToolkit/Tooltip/TooltipManipulator.cs:56-68`,
  `UIToolkit/Popup/PopupManipulator.cs:41-53`. Fix: set `_cancelled = false` before the
  immediate `Show*()` call.
  Fixed: both manipulators clear `_cancelled` before the immediate `Show*()`. No tests — these
  are UIElements manipulators needing a live panel + scheduler the suite doesn't harness.
- [x] **devconsole** — *(2 critical)* (a) `Keyboard.current[...]` null-deref every frame on
  keyboard-less sessions, `Runtime/UI/DevConsoleUIToolkit.cs:146` — add the
  `Keyboard.current == null` guard the sibling already has. (b) Binding a `bind`/`unbind`
  command to a key throws `InvalidOperationException` (dictionary mutated mid-enumeration),
  `Runtime/Core/CommandBindings.cs:62-66` — snapshot pairs before executing.
  Fixed: (a) inline `Keyboard.current != null` guard (narrower than the sibling's early-return
  so the scroll-to-bottom logic still runs); (b) `Update` collects matches into a reusable
  buffer, then executes — no mutation mid-`foreach`, no per-frame allocation.
- [x] **devconsole.netcode** — *(critical, security)* `ExecuteOnServerRpc` nulls
  `PreExecuteFilter` then executes the client-sent command → cheat/domain protection fully
  bypassed; a modified client runs Server-domain cheat commands with `sv_cheats 0`.
  `Runtime/NetworkCommandBridge.cs:112-119`. Fix: re-apply `_cheatProtected`/`CheatsEnabled`
  + `_domains` check server-side inside the RPC instead of disabling enforcement.
  Fixed: stopped nulling the filter — `Execute` now re-runs `FilterCommand` server-side, which
  re-enforces cheat/domain rules against the *resolved* command (also closes the alias-bypass an
  explicit first-token check would miss). No recursion: server-side Server-domain commands run
  locally instead of re-sending the RPC.
- [x] **ui.loading** — *(minor)* Cancellation checked only after `_scopeService.Begin()` has
  already torn down the prior scope, destroying valid UI state on a no-op cancelled run.
  `Runtime/RegisterViewsOperation.cs:64-71`. Fix: `ct.ThrowIfCancellationRequested()` before
  `Begin()`.
  Fixed: cancellation now checked before `Begin()`; regression test asserts the prior scope
  survives a cancelled run.

## P4 — README / doc drift (clear before `docs/generate.sh`)

Each is a code example that won't compile or contradicts the real API. Fix before regenerating
the docs site so published docs aren't broken. **All cleared** in a 21-agent README rewrite that
re-verified every example against the real current API per `README_STANDARD.md` (and turned up
many additional drift fixes beyond this list: assembly names → `Rubickanov.*`, dependency lists,
gas `IDisposable`/int return counts, gameplaytags engine-refs, devconsole frontend APIs, etc.).

- [x] **acs** — README documents non-existent `OnAwake` override (won't compile); class XML
  docs contradict the real `virtual Awake()` (broken `<see cref="OnAwake"/>`, false
  "non-virtual" claim). `README.md:170-177`, `Runtime/Unity/Behavior/EntityComponent.cs:13-16,36-41`.
  Fixed: README now overrides `Awake()` + `base.Awake()`; the `EntityComponent` XML/inline docs
  rewritten to the real `protected virtual void Awake()` contract (no more `OnAwake` cref / "non-virtual").
- [x] **acs.netcode** — README claims collections are unsupported, but `ObservableList`/
  `Dictionary`/`HashSet`/`RingBuffer` are fully delta-replicated. `README.md:87`.
  Fixed: added a Replicating Collections section; also added missing ObservableCollections/Unity.Collections deps and EntityRef fields.
- [x] **ui** — README uses non-existent `IUIService` methods (`ShowScreen`/`HideScreen`/…),
  `DialogResult.InputValue` (should be `InputText`). `README.md:82-83,135-146,212`.
  Fixed: rewritten to real `Show`/`Hide`/`HideTop`/`HideAll` API, `DialogResult.InputText`, real
  factory registration, spinner host, Editor assembly.
  - [x] *(minor)* `UIService.HideAll` fires visibility callback even when nothing was visible.
    `Runtime/UIService.cs:199-211`.
    Fixed: `HideAll` early-returns when nothing is shown (mirrors `HideAllAsync`), so no spurious
    `false` reaches the visibility consumer. Regression test
    `HideAll_EmptyState_DoesNotFireVisibilityCallback`.
- [x] **ui.loading** — README calls non-existent `_loadingPipeline.Run(op)`; real API is
  `Load(IReadOnlyList<ILoadingOperation>, …)`. `README.md:21`. Fixed (also dropped non-existent
  `UILayer.Dialog`).
- [x] **ui.localization** — README advertises UniTask but the package neither references nor
  uses it. `README.md:10`. Fixed: UniTask dep dropped (real deps R3 + Unity.Localization);
  bindings moved to `OnBind`, real `LocalizationKey(table, key)` ctor, Assemblies table added.
- [x] **loading** — README presenter example doesn't satisfy `ILoadingPresenter` (`Hide()`
  must return `UniTask`; missing `WaitForInput`). `README.md:92-105`. Fixed; also documented the
  two-phase `Execute`/`Activate` (`IDeferrableOperation`) pipeline and the `waitForInput` gate.
- [x] **logging** — README quick-start uses `EditorPrefs` in runtime startup code (won't
  compile in a player build). `README.md:38`. Fixed: `#if UNITY_EDITOR`-guarded; also real
  assembly names + the Unity-log dedup filter and LogType→level mapping.
- [x] **storage** — README Assemblies table omits public `PrefixedStorageService`. `README.md:24`.
  Fixed (also added the `Microsoft.Extensions.Logging.Abstractions` optional dep).

## P5 — Low / cosmetic / known limitation (opportunistic, or leave documented)

- [x] **acs.netcode** — ~~Tests asmdef not gated to Editor platform (`includePlatforms: []`).~~
  **WONTFIX — audit finding was wrong.** The `[]` is *intentional*: this assembly holds NGO
  multi-instance **PlayMode** integration tests, and `includePlatforms: []` is what keeps them
  PlayMode. Setting `["Editor"]` flips them to EditMode, where NGO's `__network_message_types`
  registry (populated only by ILPP `[RuntimeInitializeOnLoadMethod]` on play-mode entry) stays
  empty → every test dies at `StartHost()` with `Allowed Count: 0 | Index Count: 25`. Reverted
  to `[]`. Do **not** "gate to Editor" for consistency — unlike the pure-EditMode unit-test
  asmdefs elsewhere, this one must stay all-platforms.
- [x] **acs.persistence** — `GetCacheStats` XML doc contradicts implementation
  (`ScannedTypes`/`TotalFields` count the opposite of what's documented).
  `Runtime/Debug/PersistenceDebug.cs:114-140`. Fix the doc.
  Fixed: doc now describes what's actually counted (all canonical aspect types / all
  `[PersistedState]` fields from the eager reverse index, not the lazy scanner cache).
- [x] **config** — `ConfigDatabase.Get(null)` throws instead of returning null (doc says
  "returns null if not found"). `Runtime/ConfigDatabase.cs:23-31`.
  Fixed: `string.IsNullOrEmpty(id)` guard returns null; regression test added.
- [x] **devconsole** — IMGUI frontend ignores `DevConsoleSettings`. `Runtime/UI/DevConsoleIMGUI.cs:11,111`.
  Fixed: IMGUI now honors `ConsoleHeight` + `UseBuiltInToggle`/`ToggleKey` (Input System poll,
  mirroring `DevConsoleUIToolkit`) and exposes a public `Toggle()`/`SetOpen()`.
- [x] **devconsole.netcode** — `sv_cheats` registered on spawn but never unregistered on
  despawn. `Runtime/NetworkCommandBridge.cs:57-72`.
  Fixed: `OnNetworkDespawn` now unregisters `sv_cheats` (mirrors spawn-time registration).
- [x] **gameplaytags** — Code generator emits duplicate identifiers for case-differing sibling
  tags (`Damage.fire` vs `Damage.Fire`) → uncompilable generated file. `Editor/GameplayTagsGenerator.cs:169-185`.
  Fixed: `WriteNode` disambiguates colliding identifiers per scope via `MakeUniqueIdentifier`.
- [x] **localization** — Dead var `needsTableConst` (`Editor/LocalizationKeysGenerator.cs:147`);
  `default(LangLocale) != LangLocale.Empty` equality foot-gun (`Runtime/LangLocale.cs:41,45`).
  Fixed: dropped the dead var; `Equals` coalesces null/empty `Code` so `default == Empty`
  (aligns with `GetHashCode`); regression test added.
- [x] **logging** — Intercepted Unity logs echoed back to the editor console as duplicates.
  `Runtime/UnityLogInterceptor.cs:56` / `Runtime/LoggerFactoryBuilder.cs:42`.
  Fixed: `AddFilter<ZLoggerUnityDebugLoggerProvider>("Unity", LogLevel.None)` drops the "Unity"
  category from the editor Debug provider only — the file provider still records it.
- [x] **statemachine** — Unused `StateMachine.Runtime` reference in async asmdef.
  `Runtime.Async/StateMachine.Async.asmdef:4-7`. Fixed: dropped the dead reference.
- [x] **steam-transport** — Missing `Runtime/csc.rsp` (nullable annotations emit CS8632);
  `StartServer` skips `InitRelayNetworkAccess()`; `UnreliableSequenced` mapping doesn't
  preserve sequencing (Steam limitation — at least document it). `Runtime/SteamNetworkingSocketsTransport.cs`.
  Fixed: `StartServer` now calls `InitRelayNetworkAccess()`; `UnreliableSequenced` caveat
  documented at the mapping. `Runtime/csc.rsp` already present.
- [x] **storage** — Floats persisted with default ("G") format instead of round-trippable
  ("R"), risking low-bit precision loss. `Runtime/FileStorageService.cs:59`,
  `Runtime/EncryptedStorageService.cs:54`.
  Fixed: both `SetFloat` use `"R"`; regression test asserts full-precision round-trip.

## P6 — Hot-path boxing / per-frame allocations (Project Auditor follow-up)

Project Auditor flags boxing/allocation across *all* code; the vast majority is in cold paths
(one-time init, scan/reflection cached per session, editor, error-message string interp, opt-in
LINQ queries) where it amortizes to zero — **not worth fixing**. A targeted scan of the five
genuinely hot packages (acs, acs.netcode, gas, statemachine, utils) traced each candidate back
to a real per-frame/per-tick/per-entity caller and dismissed ~60 cold-path sites. Only the
below are on proven hot paths. Everything else from the Auditor report is deliberately ignored.

- [x] **gas** — *(high)* `foreach (var kvp in _attributes.All)` boxes the `Dictionary`
  enumerator every recalc: `AttributeSet.All` is typed `IEnumerable<…>`, so the struct
  `GetEnumerator` fast-path is lost and one enumerator heap-allocs per tick for any entity
  with a periodic effect (DoT/regen). `Runtime/Effects/EffectController.cs:349`
  (`RecalculateAllAttributes`, driven by `Tick`). Fix: change `AttributeSet.All` return type
  from `IEnumerable<KeyValuePair<…>>` to the concrete `Dictionary<…>` (it's internal — no
  public-API impact), or iterate the dictionary directly. Zero-alloc after.
  Fixed (superseded by the P7 gas item): rather than just retyping `AttributeSet.All` to a
  concrete `Dictionary<…>`, P7's targeted-recalc change removed `RecalculateAllAttributes` and
  `AttributeSet.All` outright — so the boxed `foreach` no longer exists at all.
- [x] **utils** — *(medium)* `EvictingPool.EvictOldest` allocates a delegate from the
  method-group `_pool.Release` on every eviction (C# 9 / Unity has no method-group caching;
  receiver is a field). Only fires when an `onEvict` callback was supplied, but then it's once
  per `Get()` at steady-state capacity — i.e. per spawn. `Runtime/Unity/EvictingPool.cs:135`.
  Fix: cache `private readonly Action<T> _releaseToPool = _pool.Release;` in the ctor and pass
  that.
  Fixed: `_releaseToPool` cached in the ctor and passed to `_onEvict` instead of `_pool.Release`.
- [x] **acs.netcode** — *(low / verify first)* `StringKeyCodec.Write` allocates a temp
  `byte[]` via `Encoding.UTF8.GetBytes` per string dictionary key replicated.
  `Runtime/Replication/Fields/ObservableDictionaryBinding.cs:40-67`. **Only** fires when a
  string-keyed `ObservableDictionary` actually has queued ops (mutation), *not* every tick —
  scan could not prove per-tick frequency, so this is steady-state-free. Fix only if profiling
  shows it: encode straight into the `FastBufferWriter` span (or stackalloc scratch for short
  keys). Read-side string alloc is unavoidable — leave it.
  Fixed: `Write` computes `GetByteCount`, then encodes via `Encoding.UTF8.GetBytes(string,
  Span<byte>)` into a 256-byte `stackalloc` buffer (heap fallback only for longer keys) — no
  per-op `byte[]` on the common short-key path. Wire format unchanged; read-side `GetString`
  left as-is. Round-trip tests cover empty / ASCII / multi-byte UTF-8 / >stack-cap keys.

**Clean (no hot-path boxing found):** `statemachine`, `acs` (no runtime enums; struct
enumerators throughout; dictionaries keyed on `Type`/`ulong` with non-boxing comparers).

## P7 — Hot-path performance (algorithmic / redundant work — Project Auditor follow-up)

Broad perf scan of the six hot packages (boxing excluded — that's P6). Every candidate was
traced to a proven per-frame/per-tick/per-entity caller; ~25 cold-path perf-shaped sites
(on-demand snapshot/restore, reflection factories cached per type, event-driven transition
double-hashes) were reviewed and dismissed. Standout is the gas one; the rest are low-confidence
and only bite at scale.

- [x] **gas** — *(high)* `Tick` recalculates the entity's **entire** attribute set on every
  tick where a periodic modifier fired or an effect expired — `RecalculateAllAttributes()` is
  O(attributes × activeEffects × modifiers), but `CurrentValue` depends only on modifiers
  targeting that same attribute, so a single regen/DoT touching one attribute pays ~Nx the
  needed work. `Runtime/Effects/EffectController.cs:245,347-355`. Fix: reuse the existing
  `_dirtyAttributes` buffer — `CollectModifierAttributes(effect.Def.Modifiers, _dirtyAttributes)`
  when a periodic fires / effect expires, then `RecalculateAttributes(_dirtyAttributes)` instead
  of `RecalculateAllAttributes()` (same targeted pattern `ApplyEffect`/`RemoveEffect` already
  use). **Pairs with the P6 gas item** — once `Tick` stops calling `RecalculateAllAttributes`,
  the `foreach` over `AttributeSet.All` no longer runs on the hot path either. Do together.
  Fixed: `Tick` collects the touched attributes and calls `RecalculateAttributes(_dirtyAttributes)`.
  `RecalculateAllAttributes` and `AttributeSet.All` (its sole consumer) are now removed — so the
  P6 gas change is superseded: the boxed `foreach` no longer exists at all. Regression tests cover
  target-attribute recalc and untouched-attribute preservation.
- [x] **acs** — *(low)* `EntityTickRunner.Update` does `_scratch.Clear()` + `AddRange(_tickables)`
  every frame even when no tickable was registered/unregistered — a full list copy per entity
  per frame purely to survive rare mid-`Tick` mutation. `Runtime/Unity/Behavior/EntityTickRunner.cs:57-58`.
  Fix: a `_dirty` flag set in Register/Unregister; rebuild `_scratch` only when dirty, else
  iterate the existing snapshot.
  Fixed: `_scratchDirty` set on real add/remove only; rebuild gated on it and cleared before
  iterating, so a mid-`Tick` mutation re-dirties for next frame (snapshot semantics preserved).
- [x] **acs.netcode** — *(low, matters only at scale)* `OwnerTick` linear-scans **all** spawned
  replicators every tick to act on the 1–2 a client owns (`if (!rep.IsOwner || rep.IsServer) continue;`).
  O(N_all) NetworkBehaviour property reads per client per tick. `Runtime/Replication/EntityReplicationSystem.cs:345-352`.
  Fix: maintain an `_ownedReplicators` list updated from Register/Unregister + OnGained/LostOwnership,
  iterate that. (Server's `ServerTick` legitimately must scan all — leave it.)
  Fixed: `_ownedReplicators` rebuilt from `_replicators` truth on an `_ownedDirty` flag (set by
  Register/Unregister + a new `MarkOwnershipChanged()` called from `OnGainedOwnership`/
  `OnLostOwnership`). `OwnerTick` iterates that subset but keeps the `IsOwner`/`IsServer` guards,
  so it stays a pure candidate-set reduction (a stale entry is skipped, never mis-sent). No new
  test — needs the live NGO ownership flow; existing host→client→host ownership tests exercise it.
- [x] **acs.netcode** — *(low)* `ObservableDictionaryBinding.ReadFrom` does `ContainsKey` then
  `Add`/indexer (double hash, full UTF-8 hash for string keys) per applied op on the receiver.
  `Runtime/Replication/Fields/ObservableDictionaryBinding.cs:319-327,334-342`. Fix: use the
  indexer `_dict[key]=value` directly for the common path; gate the `ContainsKey`
  add-vs-replace diagnostic behind a debug build.
  Fixed: both AddKey/ReplaceKey branches collapse to `_dict[key] = value` (the `ObservableDictionary`
  indexer emits the matching ObserveAdd/ObserveReplace either way); the `ContainsKey` add-vs-replace
  warning is now `#if UNITY_EDITOR || DEVELOPMENT_BUILD` only.

**Clean (no hot-path perf issues):** `utils`, `statemachine`, `acs.persistence` (all on-demand —
no `Update`/`Tick` exists in the package).

## P8 — New defects from the verification audit (code-only, fresh read)

Found by a 21-agent verification pass (one per package) that ALSO re-read every Runtime/Editor
file for new bugs. That pass confirmed **every** P1–P7 / issues.md fix above is genuinely present
and correct in code (no missing/regressed). These are NEW issues it surfaced — none were tracked
before. Docs excluded. **Clean (no new defects):** acs, acs.netcode, devconsole.config, logging,
statemachine, steam-transport, ui, ui.animations, ui.loading, ui.localization.

### Major

- [x] **gas** — *(gameplay correctness)* Periodic Duration/Infinite effects **double-count**
  their modifiers: applied to `BaseValue` every period AND simultaneously folded into the
  `CurrentValue` aggregate. `ModifierAggregator.Aggregate` skips only `Instant` effects, not
  `Period > 0` ones. Canonical Poison (Dur 5s/Period 1s/Health -3): CurrentValue drops to 97
  *immediately* before any tick, carries a phantom extra −3 the whole time, and jumps +3 on
  expiry (sign flips for a HoT). Gameplay reads CurrentValue (death checks) → wrong values.
  `Runtime/Calculation/ModifierAggregator.cs:16-19`. Fix: add `if (effect.Def.Period > 0f) continue;`
  next to the `Instant` skip — periodic effects contribute only via their periodic BaseValue writes.
  Fixed: `Period > 0f` skip added in `Aggregate`; regression test
  `EffectControllerTickTests.Tick_PeriodicEffect_CurrentValueDoesNotDoubleCountModifier` asserts
  CurrentValue == BaseValue immediately after apply and after one period (no phantom −3).
- [x] **devconsole.netcode** — *(security / remote DoS)* The cheat-protection fix is solid, but
  `ExecuteOnServerRpc`'s server-side `FilterCommand` only special-cases the **Server** domain; for
  a Client/Shared-domain command it returns "execute locally", so the server runs it. A modified
  client can `ExecuteOnServerRpc("disconnect")` → `disconnect` is Client-domain and calls
  `NetworkManager.Shutdown()` with no server guard → shuts down the whole server. Any non-Server
  command becomes server-executable. `Runtime/NetworkCommandBridge.cs:111-137` +
  `Runtime/Commands/NetworkCommands.cs:102-115`. Fix: in the RPC, reject any command whose resolved
  `_domains` value is not `Server` (send an error) before calling `Execute`.
  Fixed: `FilterCommand`'s Client/Shared branch now returns an Error when `ExecutingClientId` is set
  (i.e. the command arrived via `ExecuteOnServerRpc`), so only Server-domain commands run on the
  server for a remote client. Checked against the resolved command → alias-safe. Local/host
  execution (`ExecutingClientId == null`) is unaffected. No test (package has no Tests/ folder).
- [x] **config** — *(invariant violation)* Duplicate-Id `ConfigDatabase` throws only on the
  **first** `Get`: `BuildLookup` assigns `_lookup` to its partially-built dictionary *before* the
  post-loop duplicate check throws, so `_lookup != null` afterwards and every later `Get` skips
  the build and silently returns the first-seen item. Same input → throw, then succeed.
  `Runtime/ConfigDatabase.cs:92-118`. Fix: build into a local and assign `_lookup` only after the
  duplicate check passes (or null `_lookup` before throwing).
  Fixed: `BuildLookup` builds into a local `lookup` and assigns `_lookup` only after the duplicate
  check passes, so a duplicate-Id database throws on *every* `Get` (never silently succeeds).
  Regression test `Get_DuplicateIds_ThrowsOnEverySubsequentCall` covers the repeat-call invariant.
- [x] **devconsole** — *(crash)* `GetSuggestions` only early-returns for `IsNullOrEmpty(input)`;
  an input that tokenizes to zero tokens (leading space `" "`, or a quote char first) leaves the
  token buffer empty and `_tokenBuffer[0]` throws `IndexOutOfRangeException` — fires on a normal
  keystroke from both frontends. `Runtime/Core/CommandRegistry.cs:554`. Fix: `if (_tokenBuffer.Count == 0) return;`
  after `Tokenize`.
  Fixed: zero-token guard added right after `Tokenize`; regression test
  `GetSuggestionsTests.GetSuggestions_WhitespaceOrQuoteOnlyInput_DoesNotThrow` covers `" "` and `"\""`.
- [x] **loading** — *(resource leak)* Cancelling a `LoadSceneOperation` (a first-class flow)
  throws out of the `progress < 0.9f` spin with `_asyncOp` holding a ~90%-loaded, never-activated,
  never-unloaded scene. Permanent for `Additive` (no later Single-load evicts it); repeated
  cancels accumulate. `Runtime/LoadSceneOperation.cs:34-50`. Fix: on cancel, set
  `allowSceneActivation = true`, await `isDone`, `UnloadSceneAsync`, then rethrow.
  Fixed: the spin loop now catches `OperationCanceledException` and calls `UnloadPartialScene` —
  activates the partial scene (so the async op can finish), awaits `isDone`, then
  `UnloadSceneAsync` before rethrowing. Guarded with `sceneCount > 1` so it never tries to unload
  the last scene (Unity forbids it; in Single mode the activated scene already replaced the prior
  one). No test — `SceneManager` needs the live Unity runtime the EditMode suite can't harness.
- [x] **localization** — *(editor codegen, uncompilable output)* The key generator does no
  per-scope identifier de-duplication, so realistic table layouts emit duplicate member names
  (CS0102): leaf-vs-nested-class (`menu.settings` + `menu.settings.volume`), case-differing
  siblings (`item.fire` + `item.Fire`), `my-key` + `my_key`. This is the exact bug fixed in
  `gameplaytags` (`MakeUniqueIdentifier`) but never ported here. `Editor/LocalizationKeysGenerator.cs:141-197`.
  Fix: mirror gameplaytags' per-scope `usedNames` + `MakeUniqueIdentifier`.
  Fixed: ported `MakeUniqueIdentifier` + a per-scope `usedNames` set covering the `Table` const,
  leaf fields, and nested child classes (plus a separate set for the table classes themselves);
  member names re-sorted Ordinal for cross-machine determinism. `GenerateCode` extracted to a pure
  `(IReadOnlyDictionary tables, LocalizationCodeOptions)` overload so it's unit-testable. Folds in
  the minor `Table`-collision item below. Regression tests cover all four collision shapes +
  determinism in `LocalizationKeysGeneratorTests`.

### Minor

- [x] **storage** — Saves rewrite the whole file in place (`File.WriteAllTextAsync`, no temp+rename).
  A crash mid-write truncates the file → next load can't parse it → renamed `.corrupt.bak`, save
  lost entirely. `Runtime/FileStorageService.cs:134`. Fix: write to `.tmp` then `File.Replace`/`Move`.
  Fixed: `ChainSave` writes to `<file>.tmp` then swaps it in via `File.Replace` (atomic where
  supported) — or `File.Move` for the first-ever save — so a crash mid-write truncates the temp,
  never the live file. Regression tests assert no `.tmp` lingers and an over-write replaces content.
- [x] **acs.persistence** — Dictionary restore from a duplicate-key source (a list-of-pairs shape
  the permissive cast intentionally accepts) throws `ArgumentException` on the 2nd `Add`, which is
  NOT in the per-field restore catch filter (only `InvalidCastException`/`NRE`) → aborts the whole
  entity *and* world restore, leaving the dict half-populated. `Runtime/Bindings/PersistedDictionaryBinding.cs:38-40`.
  Fix: upsert (`_collection[k]=v`) instead of `Add`, or widen the catch filter.
  Fixed: `WriteValue` upserts via the indexer (`_collection[k]=v`) instead of `Add`, so a
  duplicate-key source can't throw mid-restore (last value wins). Regression test
  `Restore_ObservableDictionary_DuplicateKeySource_UpsertsWithoutAborting` covers it.
- [x] **config** — `Dispose()` racing an in-flight `LoadAsync` re-populates `_cache` after release
  → that Addressables handle leaks for the process lifetime. `Runtime/ConfigService.cs:93-143`.
  Fix: cancel in-flight loads in Dispose, or re-check `_disposed` after the await and release.
  Fixed: `LoadInternalAsync` re-checks `_disposed` after the load await — if `Dispose` ran during
  the load it releases the fresh handle and throws `ObjectDisposedException` instead of caching it.
  No unit test — needs the live Addressables runtime to drive a real in-flight handle.
- [x] **config** — Coalesced `LoadAsync` callers share the *first* caller's cancellation token; if
  caller A cancels, caller B's un-cancelled load also fails. `Runtime/ConfigService.cs:48-56,104-121`.
  Fix: linked/ref-counted token, or document the shared-cancellation semantics.
  Fixed: the coalesced load now runs on `CancellationToken.None`; each caller (initiator and
  joiners) attaches its own token via `AttachExternalCancellation(ct)`, so one caller cancelling
  only faults its own await — the shared load runs to completion and caches for the others.
- [x] **audio** — `DuckSFX` doesn't restore the SFX mixer param on cancel / `Dispose` mid-duck →
  the shared `AudioMixer` is left attenuated. `Runtime/UnityAudioService.cs:644,650-678`. Fix:
  re-apply `_sfxVolume` in the `OperationCanceledException` branch and after cancelling `_duckCts`
  in Dispose.
  Fixed: `DuckAsync`'s `OperationCanceledException` branch now restores `_sfxVolume` before
  bailing; `Dispose` cancels `_duckCts` and re-applies `_sfxVolume` synchronously (the externally
  owned mixer outlives the service, so the async restore alone could leave it attenuated). No test
  — the duck path needs a live `AudioMixer` asset the suite's null-mixer config can't provide.
- [x] **gameplaytags** — Generator emits a duplicate `Tag` member when a branch tag has a child
  segment that sanitizes to `Tag` (e.g. path `Damage.Tag`): the auto-emitted `Tag` field isn't in
  the child scope's `usedNames`. `Editor/GameplayTagsGenerator.cs:147-167`. Fix: seed the child
  scope's reserved set with `"Tag"`.
  Fixed: `WriteNode` takes a `reserveTag` flag — recursive (nested-class) scopes seed `usedNames`
  with `"Tag"` so a child segment sanitizing to `Tag` gets `MakeUniqueIdentifier`-suffixed
  (`Tag_2`) instead of colliding; the top-level class scope (no emitted `Tag` field) passes false.
  Regression test `GenerateCode_ChildSegmentSanitizingToTag_DoesNotCollideWithEmittedTagField`.
- [x] **localization** — A leaf/child key that sanitizes to `Table` collides with the reserved
  `private const string Table`. `Editor/LocalizationKeysGenerator.cs:149,156`. Fix: rename the
  const (`__Table`) or reserve it in the per-scope set (folds into the Major localization fix).
  Fixed as part of the Major localization fix above: scopes that emit the `Table` const seed their
  per-scope `usedNames` with `"Table"`, so a colliding key is `MakeUniqueIdentifier`-suffixed
  (`Table_2`). Covered by `GenerateCode_KeySanitizingToTable_DoesNotCollideWithTableConst`.
- [x] **loading** — Linked `CancellationTokenSource` isn't disposed after a `Load` completes,
  leaving one registration on the caller's token until the next Load/Dispose.
  `Runtime/LoadingService.cs:53-55,91-96`. Fix: dispose+null `_cts` in the Load finally when the
  generation still matches.
  Fixed: the `Load` finally disposes+nulls `_cts` when `_loadGeneration == generation` (the latest
  Load owns it; a newer Load has already cancelled+replaced it), so the linked registration on the
  caller's token is released as soon as the Load resolves.
- [x] **utils** — `DeterministicRandom.Int(...)` overflows when `maxExclusive - min > int.MaxValue`
  (int subtraction before the uint cast) → out-of-range result. `Runtime/DeterministicRandom.cs:76,87`.
  Fix: compute the range as `(uint)((long)maxExclusive - min)`.
  Fixed: both `Int` overloads widen the range to `(uint)((long)maxExclusive - min)` before the
  modulo; unchecked `min + (int)(...)` wraps back into `[min, maxExclusive)`. Regression tests
  cover the full int range and a range wider than `int.MaxValue` for both overloads.
