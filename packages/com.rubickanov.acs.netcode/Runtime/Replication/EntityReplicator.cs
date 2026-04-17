using System;
using R3;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Rubickanov.ACS.Runtime.Netcode
{
    // How ApplyStateBuffer decides whether to apply each incoming owner-auth field.
    // Server-auth fields are always applied regardless of the mode — the server is
    // the sole writer, so there is no local-write to preserve on the receiving end.
    internal enum StateApplyMode
    {
        // Apply every field in the payload, server-auth and owner-auth alike. Used
        // only by tests that set up a synthetic replicator with no owner-auth state
        // to protect.
        ApplyAll,
        // Skip every owner-auth field unconditionally. The normal broadcast path
        // passes this on peers that own the entity: they hold fresher local values
        // than whatever the server relays, so server copies must be discarded.
        SkipOwnerAuth,
        // Skip an owner-auth field only if this peer has already written to it
        // locally since spawn/ownership (OwnerWroteSinceSpawn == true). Used by the
        // initial-sync path: lets server-preset values (e.g. WeaponId) reach a
        // pure-client owner that has not touched the field yet, while protecting
        // any fresh local write that landed between RequestInitialStateRpc and
        // SendInitialStateRpc.
        SkipOwnerAuthIfLocallyWritten,
    }

    [DisallowMultipleComponent]
    public partial class EntityReplicator : NetworkBehaviour
    {
        [SerializeField]
        [Tooltip("Render delay for interpolated fields, in ticks. Default 2 — lower trades smoothness for latency, higher masks packet jitter. Shooter-grade setups may prefer 1.")]
        private int _interpolationDelayTicks = 2;

        private ReplicatedFieldBinding[] _bindings = Array.Empty<ReplicatedFieldBinding>();
        private AuthorityMode[] _bindingAuthorities = Array.Empty<AuthorityMode>();
        private ReplicatedFieldBinding[] _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
        private ReplicatedEventBinding[] _eventBindings = Array.Empty<ReplicatedEventBinding>();
        private NetworkScopeController? _scopeController;
        // Built lazily at spawn so OnNetworkSpawn can reuse scratch buffers across
        // despawn/respawn cycles. Held after the first spawn.
        private BindingsBuilder? _bindingsBuilder;
        private DisposableBag _disposables;
        private DisposableBag _ownerDisposables;
        private double _tickInterval;
        private double _interpolationDelaySeconds;
        // Cached at spawn to detect runtime TickRate mutation. The binding windows
        // (AuthorityRenderBinding coalesce / stale, interpolation delay, prediction tick
        // delta) are sized against the spawn-time tick rate and cannot be recomputed
        // cheaply after the fact — the correct fix is to respawn the entity, so we
        // warn instead of silently drifting.
        private uint _tickRateAtSpawn;
        private static bool s_tickRateDriftWarned;

        // Play-Mode-without-Domain-Reload safety: reset the one-shot latch on subsystem
        // registration so a second Enter-Play without domain reload sees drift warnings again.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_tickRateDriftWarned = false;
        }
        // Offset applied to incoming owner-auth senderTick to convert from the client's
        // estimated ServerTime to the server's authoritative time base. See
        // OwnerSubmitTickSync for the EMA math; the replicator only owns the instance and
        // resets it on despawn / ownership change.
        private OwnerSubmitTickSync _ownerSubmitTickSync;
        // Pre-size floor for the initial-sync FastBufferWriter. The writer does NOT
        // auto-grow past its initial capacity unless maxSize > size is specified
        // (see FastBufferWriter.Grow), and collection bindings' SnapshotSize depends
        // on their live element count — so we compute the snapshot size at spawn as
        // a starting lower bound but recompute in InitialSyncPayloadHint on access
        // so growth after spawn is captured. Not a runtime cap on the tick path.
        private int _initialSyncPayloadFloorAtSpawn;
        private int _maskByteCount;
        private byte[] _dirtyMaskBuffer = Array.Empty<byte>();
        private EntityReplicationSystem? _system;
        // Cached at spawn so the EntityId accessor does not re-walk GetComponent<MonoEntity>()
        // on every call. Null after OnNetworkDespawn; consumers must guard via EntityId.IsNone.
        private MonoEntity? _monoEntity;

        /// <summary>
        /// Domain <see cref="EntityId"/> of the entity this replicator belongs to. Read from
        /// the sibling <see cref="MonoEntity"/> captured at spawn. Returns <see cref="EntityId.None"/>
        /// before <see cref="OnNetworkSpawn"/> and after <see cref="OnNetworkDespawn"/>.
        /// Exposed so <see cref="EntityReplicationSystem"/> can index replicators by EntityId
        /// for the EntityRef codec translation path.
        /// </summary>
        internal EntityId EntityId => _monoEntity != null ? _monoEntity.Id : EntityId.None;

        // Prediction bookkeeping. _predictedFields and _predictedBindingIndices are
        // captured at spawn so the snapshot/capture path can walk the right bindings
        // without re-scanning reflection. _predictionBinder owns the typed
        // PredictionManager<TInput> hook and the TInput resolution — see
        // PredictionBinder for the caching + registration lifecycle.
        //
        // Per-entity (not prefab-scoped) by design: each replicator owns an independent
        // PredictionManager<TInput> input buffer and snapshot ring, which are per-entity
        // state by definition. The reflection cost amortizes through PredictionHookCache's
        // per-type cache, so the only truly per-entity work here is the small allocation of
        // a PredictionBinder shell — cheap compared to the NetworkObject spawn it rides.
        private PredictedFieldInfo[] _predictedFields = Array.Empty<PredictedFieldInfo>();
        // Indices into _bindings that correspond to [Replicated(Predicted = true)] fields. Built in
        // OnNetworkSpawn by joining PredictionScanner output with ReplicationScanner
        // output on field name. Drives step 7's snapshot capture path.
        private int[] _predictedBindingIndices = Array.Empty<int>();
        // Σ _bindings[_predictedBindingIndices[i]].Size. Sizes the per-entity
        // SnapshotBuffer slots in PredictionManager.
        private int _predictedPayloadSize;
        private PredictionBinder? _predictionBinder;

        // Internal surface exposed to EntityReplicationSystem.
        internal ReplicatedFieldBinding[] Bindings => _bindings;
        internal AuthorityMode[] BindingAuthorities => _bindingAuthorities;
        internal ReplicatedEventBinding[] EventBindings => _eventBindings;
        internal int InitialSyncPayloadHint
        {
            get
            {
                // Recompute at request time so ObservableList bindings that grew since
                // spawn size the FastBufferWriter correctly. For scalar-only entities
                // this is O(bindings) with all constant-time SnapshotSize — negligible,
                // and only hit on initial-sync requests (not per tick).
                int payloadHint = sizeof(int) + _maskByteCount;
                for (int i = 0; i < _bindings.Length; i++)
                    payloadHint += _bindings[i].SnapshotSize;
                // Floor guards against shrinking below the spawn-time estimate — defensive
                // only; SnapshotSize should already be monotonic across scalar bindings.
                return payloadHint > _initialSyncPayloadFloorAtSpawn ? payloadHint : _initialSyncPayloadFloorAtSpawn;
            }
        }
        internal int MaskByteCount => _maskByteCount;
        internal byte[] DirtyMaskBuffer => _dirtyMaskBuffer;

        // Exposed so step 7's snapshot buffer can reuse the scan without re-walking reflection.
        internal PredictedFieldInfo[] PredictedFields => _predictedFields;
        internal Type? PredictedInputType => _predictionBinder?.InputType;
        internal int[] PredictedBindingIndices => _predictedBindingIndices;
        internal int PredictedPayloadSize => _predictedPayloadSize;

        public override void OnNetworkSpawn()
        {
            // Apply [NetworkScope] first so ServerOnly / OwnerOnly components stop ticking
            // as early as possible on peers where they should not run. NGO does not guarantee
            // OnNetworkSpawn order between NetworkBehaviours on the same NetworkObject, but
            // EntityNetworkComponent routes subscribe/dispose through OnEnable/OnDisable: if
            // a sibling's OnNetworkSpawn happened to fire before us its subscription is
            // released synchronously via OnDisable when we flip enabled=false here — before
            // any aspect event can fire. The reverse order stays silent because TrySubscribe
            // also checks enabled. Regression #16.
            ApplyNetworkScopes();

            _monoEntity = GetComponent<MonoEntity>();
            if (_monoEntity == null)
            {
                Debug.LogError($"[EntityReplicator] '{gameObject.name}' is missing MonoEntity on the root. Replication disabled.");
                return;
            }
            var context = _monoEntity;

            // Resolve tick interval up front — AuthorityRenderBinding's coalesce / stale
            // windows are sized relative to it. tickRate == 0 is the degenerate path;
            // bindings still construct but with tickDelta = 0 the authority-render windows
            // collapse and the interpolated bindings list is cleared below so TickRender
            // never runs.
            uint tickRate = NetworkManager.NetworkTickSystem.TickRate;
            _tickRateAtSpawn = tickRate;
            _tickInterval = tickRate > 0 ? 1.0 / tickRate : 0;
            _interpolationDelaySeconds = _interpolationDelayTicks * _tickInterval;

            // Resolve the replication system up-front so the binding loop can inject it
            // into the factory for EntityRef-typed fields (EntityRefCodec needs an
            // IEntityRefResolver reference at construction time). GetOrCreate is
            // idempotent per NetworkManager; Register further down still does the
            // per-replicator hookup.
            _system = EntityReplicationSystem.GetOrCreate(NetworkManager);

            _bindingsBuilder ??= new BindingsBuilder();
            var built = _bindingsBuilder.Build(
                context,
                gameObject.name,
                IsServer,
                IsOwner,
                _tickInterval,
                _system,
                ref _disposables,
                ref _ownerDisposables);
            _bindings = built.Bindings;
            _bindingAuthorities = built.BindingAuthorities;
            _eventBindings = built.EventBindings;
            _interpolatedBindings = built.InterpolatedBindings;

            // Regression guard: the dirty-mask bit and every event-id on the
            // wire is indexed by position in the binding array. If two peers each had to drop
            // a *different* excess binding (say one truncated an aspect the other was missing
            // for a mod/version/config reason) the bitmask positions would drift and incoming
            // payloads would land on the wrong fields — silent state corruption with no log.
            // Aborting spawn is strictly safer than proceeding with a truncated binding list.
            if (ExceedsFieldBindingCap(_bindings.Length, gameObject.name))
            {
                // Null the early-resolved _system so the abort path is observably
                // identical to the pre-change behaviour: a caller that inspects
                // _system to verify "did this replicator register?" must still see null.
                _system = null;
                return;
            }

            // Compute initial-sync payload hint AFTER the cap check so it reflects
            // exactly the bindings that will actually be written.
            _maskByteCount = (_bindings.Length + 7) / 8;
            _dirtyMaskBuffer = new byte[_maskByteCount];
            int payloadHint = sizeof(int) + _maskByteCount;
            for (int i = 0; i < _bindings.Length; i++)
                payloadHint += _bindings[i].SnapshotSize;
            _initialSyncPayloadFloorAtSpawn = payloadHint;

            // Interpolation timing was resolved up-front (before the binding loop) so
            // AuthorityRenderBinding could receive tickDelta at construction. On a
            // degenerate tick rate, Update's TickRender loop must still bail —
            // drop the interpolated list now that the loop has flattened it.
            if (_tickInterval == 0)
                _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();

            if (ExceedsEventBindingCap(_eventBindings.Length, gameObject.name))
            {
                _system = null;
                return;
            }

            _predictedFields = built.PredictedFields;
            _predictedBindingIndices = built.PredictedBindingIndices;

            int predictedPayload = 0;
            for (int i = 0; i < _predictedBindingIndices.Length; i++)
                predictedPayload += _bindings[_predictedBindingIndices[i]].Size;
            _predictedPayloadSize = predictedPayload;

            // Prediction must bootstrap BEFORE the replication system so PredictionManager's
            // Tick handler is queued first this frame (Simulate writes → marks dirty →
            // ServerTick drains in the same tick). PredictionManager.RequeueTick handles
            // the reverse order when the replication system was created earlier by an
            // entity without predicted fields.
            BootstrapPrediction();

            // Register with the centralized replication system. _system was resolved
            // earlier (before the binding loop) so EntityRefCodec could be injected;
            // here we complete the per-replicator hookup.
            _system.Register(this);

            // Subscribe event bindings that this peer is authority for.
            SubscribeEventBindingsAsAuthority();

            // Late-joining clients miss state for fields that never go dirty after spawn
            // (MaxHealth, WeaponId, TeamColor). Pull a full snapshot from the server.
            // Host (IsServer) skip: already has the latest values locally.
            if (!IsServer && _bindings.Length > 0)
                _system.RequestInitialSync(this);

            // Regression guard: bindings length, authority count, mask byte count, dirty-mask
            // buffer length, and every predicted index must line up. They do today by
            // construction, but a future reorder or new clamp could desync them silently — and
            // a drift here corrupts every peer's decoded state because mask bit positions map
            // straight to binding indices. Throw unconditionally (not Debug.Assert) so shipping
            // builds fail loud at spawn instead of producing misrouted payloads.
            if (_bindings.Length != _bindingAuthorities.Length)
                throw new InvalidOperationException(
                    $"[EntityReplicator] bindings/authorities length drift: " +
                    $"{_bindings.Length} vs {_bindingAuthorities.Length} on '{gameObject.name}'.");
            if (_maskByteCount != (_bindings.Length + 7) / 8)
                throw new InvalidOperationException(
                    $"[EntityReplicator] mask byte count does not match binding count: " +
                    $"maskBytes={_maskByteCount}, bindings={_bindings.Length} on '{gameObject.name}'.");
            if (_dirtyMaskBuffer.Length != _maskByteCount)
                throw new InvalidOperationException(
                    $"[EntityReplicator] dirty mask buffer size drift: " +
                    $"buffer={_dirtyMaskBuffer.Length}, expected={_maskByteCount} on '{gameObject.name}'.");
            for (int i = 0; i < _predictedBindingIndices.Length; i++)
                if (_predictedBindingIndices[i] >= _bindings.Length)
                    throw new InvalidOperationException(
                        $"[EntityReplicator] predicted index {_predictedBindingIndices[i]} out of range " +
                        $"(bindings.Length={_bindings.Length}) on '{gameObject.name}'.");
        }

        // Extracted so the cap invariants are unit-testable without spinning up a
        // NetworkManager, and so the field + event paths read identically.
        //
        // Why the cap is exactly 256 on both:
        // - Event indices are packed into a byte on the wire (see the (byte)i cast in
        //   SubscribeAsAuthority's subscribe loop). At i == 256 the cast wraps to 0 and
        //   binding #256 collides with binding #0 — peers would silently route event 256's
        //   payload into event 0's Subject<T> with no crash and no log.
        // - Field bitmask positions are encoded the same way (pack/unpack is by array
        //   index). Truncating one peer's binding list but not another's (e.g. a mod or
        //   config difference) would drift the mask positions between peers and write
        //   network payloads into the wrong reactive properties.
        //
        // Both helpers are predicates: they do not mutate the caller's array. On overflow
        // OnNetworkSpawn returns without registering with the replication system, which
        // is strictly safer than silent truncation.

        internal static bool ExceedsFieldBindingCap(int bindingCount, string entityName)
        {
            if (bindingCount > 256)
            {
                Debug.LogError($"[EntityReplicator] Entity '{entityName}' has {bindingCount} replicated fields; max is 256 (valid indices 0..255, wire format uses one dirty-mask byte per 8 fields). Aborting spawn — this replicator will not register with the replication system.");
                return true;
            }
            return false;
        }

        internal static bool ExceedsEventBindingCap(int bindingCount, string entityName)
        {
            if (bindingCount > 256)
            {
                Debug.LogError($"[EntityReplicator] Entity '{entityName}' has {bindingCount} replicated events; max is 256 (valid indices 0..255, wire format packs the index into a single byte). Aborting spawn — this replicator will not register with the replication system.");
                return true;
            }
            return false;
        }

        public override void OnNetworkDespawn()
        {
            _predictionBinder?.Unregister();

            _system?.Unregister(this);
            _system = null;
            _ownerSubmitTickSync.Reset();

            for (int i = 0; i < _bindings.Length; i++)
                _bindings[i].OnDespawn();
            for (int i = 0; i < _eventBindings.Length; i++)
                _eventBindings[i].OnDespawn();

            _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
            _ownerDisposables.Dispose();
            _disposables.Dispose();

            // Null the cached MonoEntity AFTER Unregister so EntityId is still readable
            // while the system tears down its _byEntityId index.
            _monoEntity = null;
        }

        private void Update()
        {
            if (!IsSpawned) return;

            // Runtime TickRate drift guard. Spawn-time tick rate is baked into interpolation
            // delay, AuthorityRenderBinding windows, and PredictionManager._tickDelta — none of
            // which we can recompute in place. Fire once per session (static flag) to avoid
            // log spam when many replicators tick past a just-changed rate. Fix: set TickRate
            // before the first entity spawns and keep it constant, or respawn affected entities.
            if (!s_tickRateDriftWarned && NetworkManager.NetworkTickSystem.TickRate != _tickRateAtSpawn)
            {
                s_tickRateDriftWarned = true;
                Debug.LogWarning(
                    $"[EntityReplicator] NetworkTickSystem.TickRate changed after spawn " +
                    $"({_tickRateAtSpawn} → {NetworkManager.NetworkTickSystem.TickRate}) on '{gameObject.name}'. " +
                    $"Interpolation delay, authority-render windows, and prediction tick delta are sized at spawn " +
                    $"and will not update for already-spawned replicators. Respawn replicated entities to pick up " +
                    $"the new tick rate.");
            }

            if (_interpolatedBindings.Length == 0) return;

            double renderTime = NetworkManager.ServerTime.Time - _interpolationDelaySeconds;
            for (int i = 0; i < _interpolatedBindings.Length; i++)
                _interpolatedBindings[i].TickRender(renderTime);
        }

        public override void OnGainedOwnership()
        {
            // Re-evaluate [NetworkScope(OwnerOnly)] FIRST so sibling components are enabled
            // before the aspect-level subscriptions below fire. OnNetworkSpawn orders it the
            // same way (ApplyNetworkScopes → binding construction) to avoid scope-disabled
            // components missing their first event; keeping the same order here fixes the
            // mirror case at ownership transfer.
            ReapplyOwnerScope();

            // Subscribe owner-auth field and event bindings now that this peer
            // is the authority. Previous owner's subscriptions were disposed in
            // their OnLostOwnership.
            SubscribeOwnerFieldBindings();
            SubscribeEventBindingsAsAuthority();

            // Reset the tick offset — a new owner has a different clock drift.
            _ownerSubmitTickSync.Reset();

            // Flip owner-auth bindings back into "sample-from-network" mode by dropping their
            // subscribe-sampler flag. We intentionally do NOT wipe the _prev/_curr render pair
            // (see OnAuthorityLost): preserving the last few samples lets the wall-clock
            // smoothing carry the view across the transfer instead of snapping.
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindingAuthorities[i] == AuthorityMode.Owner)
                    _bindings[i].OnAuthorityLost();
            }
        }

        public override void OnLostOwnership()
        {
            // Tear down owner-auth subscriptions — this peer is no longer the
            // authority for those fields/events.
            _ownerDisposables.Dispose();
            _ownerDisposables = default;

            // Flip owner-auth bindings into "sample-from-network" mode. The subscribe-side
            // sampler that fed AuthorityRenderBinding's render pair is gone now; without this,
            // InterpolatedValue would freeze on the last local write because ApplyFromNetwork
            // skips sampling whenever _samplesFromSubscribe is set. Unlike the old
            // ClearInterpolationState path we keep the existing render pair so the first
            // incoming relayed snapshot can smoothly slide onto _prev/_curr.
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindingAuthorities[i] == AuthorityMode.Owner)
                    _bindings[i].OnAuthorityLost();
            }

            ReapplyOwnerScope();
        }

        private void SubscribeOwnerFieldBindings()
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindingAuthorities[i] != AuthorityMode.Owner) continue;
                _bindings[i].SubscribeAsAuthority(ref _ownerDisposables);
                _bindings[i].ResetOwnerWroteSinceSpawn();
            }
        }

        private void ApplyNetworkScopes()
        {
            _scopeController ??= new NetworkScopeController(NetworkObject);
            _scopeController.ApplyInitial(IsServer, IsOwner);
        }

        private void ReapplyOwnerScope()
        {
            _scopeController?.ReapplyOwner(IsOwner);
        }
    }
}
