using System;
using System.Collections.Generic;
using System.Reflection;
using R3;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

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
        // SendInitialStateRpc. See ISSUES.md #19.
        SkipOwnerAuthIfLocallyWritten,
    }

    [DisallowMultipleComponent]
    public class AspectReplicator : NetworkBehaviour
    {
        [SerializeField]
        [Tooltip("Render delay for interpolated fields, in ticks. Default 2 — lower trades smoothness for latency, higher masks packet jitter. Shooter-grade setups may prefer 1.")]
        private int _interpolationDelayTicks = 2;

        private ReplicatedFieldBinding[] _bindings = Array.Empty<ReplicatedFieldBinding>();
        private AuthorityMode[] _bindingAuthorities = Array.Empty<AuthorityMode>();
        private ReplicatedFieldBinding[] _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
        private ReplicatedEventBinding[] _eventBindings = Array.Empty<ReplicatedEventBinding>();
        private Behaviour[] _ownerScopedComponents = Array.Empty<Behaviour>();
        private readonly List<IEntityComponent> _scopeComponentsBuffer = new();
        // Scratch collections reused across spawns to eliminate the per-spawn
        // allocation cloud in OnNetworkSpawn (see ISSUES.md #10). Mirrors the
        // _scopeComponentsBuffer pattern: Clear() at point of use, populate,
        // then ToArray() for the handful that back instance fields. Per-aspect
        // scratch (names / bindingByName) is Clear()-ed inside the aspect loop.
        private readonly List<ReplicatedFieldBinding> _bindingsScratch = new();
        private readonly List<AuthorityMode> _bindingAuthoritiesScratch = new();
        private readonly List<ReplicatedFieldBinding> _interpolatedBindingsScratch = new();
        private readonly List<ReplicatedEventBinding> _eventBindingsScratch = new();
        private readonly List<PredictedFieldInfo> _predictedFieldsScratch = new();
        private readonly List<int> _predictedBindingIndicesScratch = new();
        private readonly List<object> _aspectListScratch = new();
        private readonly HashSet<string> _predictedFieldNamesScratch = new();
        private readonly Dictionary<string, int> _aspectBindingByNameScratch = new();
        private readonly List<Behaviour> _ownerScopedScratch = new();
        private readonly List<MonoBehaviour> _behavioursScratch = new();
        // Prefab-level cache for ResolveInputType (see ISSUES.md #12). Keyed by
        // NetworkObject.PrefabIdHash — stable per-prefab — so a spawn wave of
        // identical prefabs does the reflection walk once. Cleared by Batch 8's
        // ResetStatics sweep.
        private static readonly Dictionary<uint, Type?> s_InputTypeCache = new();

        // Play-Mode-without-Domain-Reload safety (ISSUES.md #17 / TODO.md Batch 8).
        // Matching clear lives on the nested PredictionHookCache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_InputTypeCache.Clear();
        }
        private DisposableBag _disposables;
        private DisposableBag _ownerDisposables;
        private double _tickInterval;
        private double _interpolationDelaySeconds;
        // Offset applied to incoming owner-auth senderTick to convert from the client's
        // estimated ServerTime to the server's authoritative time base. Seeded on the
        // first owner submission: offset = serverTick - senderTick. Subsequent
        // submissions feed an EMA (alpha = 0.1) so the offset tracks NGO's mid-session
        // client-clock re-syncs without losing the even spacing of client ticks.
        // The companion bool replaces the int sentinel that the old int-typed field used.
        private double _ownerSubmitTickOffset;
        private bool _hasOwnerSubmitTickOffset;
        // Pre-size floor for the initial-sync FastBufferWriter. The writer does NOT
        // auto-grow past its initial capacity unless maxSize > size is specified
        // (see FastBufferWriter.Grow), and collection bindings' SnapshotSize depends
        // on their live element count — so we compute the snapshot size at spawn as
        // a starting lower bound but recompute in InitialSyncPayloadHint on access
        // so growth after spawn is captured. Not a runtime cap on the tick path.
        private int _initialSyncPayloadFloorAtSpawn;
        private int _maskByteCount;
        private byte[] _dirtyMaskBuffer = Array.Empty<byte>();
        private AspectReplicationSystem? _system;
        // Cached at spawn so the EntityId accessor does not re-walk GetComponent<MonoEntity>()
        // on every call. Null after OnNetworkDespawn; consumers must guard via EntityId.IsNone.
        private MonoEntity? _monoEntity;

        /// <summary>
        /// Domain <see cref="EntityId"/> of the entity this replicator belongs to. Read from
        /// the sibling <see cref="MonoEntity"/> captured at spawn. Returns <see cref="EntityId.None"/>
        /// before <see cref="OnNetworkSpawn"/> and after <see cref="OnNetworkDespawn"/>.
        /// Exposed so <see cref="AspectReplicationSystem"/> can index replicators by EntityId
        /// for the EntityRef codec translation path.
        /// </summary>
        internal EntityId EntityId => _monoEntity != null ? _monoEntity.Id : EntityId.None;

        // Prediction bookkeeping. Captured at spawn so OnNetworkDespawn can route
        // the unregister call to the right PredictionManager<TInput> instance even
        // though AspectReplicator itself is not generic. Null when no predicted
        // fields exist on this entity.
        private PredictedFieldInfo[] _predictedFields = Array.Empty<PredictedFieldInfo>();
        private Type? _predictedInputType;
        // Indices into _bindings that correspond to [Replicated(Predicted = true)] fields. Built in
        // OnNetworkSpawn by joining PredictionScanner output with ReplicationScanner
        // output on field name. Drives step 7's snapshot capture path.
        private int[] _predictedBindingIndices = Array.Empty<int>();
        // Σ _bindings[_predictedBindingIndices[i]].Size. Sizes the per-entity
        // SnapshotBuffer slots in PredictionManager.
        private int _predictedPayloadSize;
        // Typed prediction manager view resolved once at BootstrapPrediction via
        // PredictionHookCache (one reflective Invoke per entity lifetime). Reconcile
        // and despawn route through this reference directly — no MethodInfo.Invoke
        // on the hot path. Null on peers/entities without predicted input type.
        private IAspectPredictionHook? _predictionHook;

        // Internal surface exposed to AspectReplicationSystem.
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
        internal Type? PredictedInputType => _predictedInputType;
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
                Debug.LogError($"[AspectReplicator] '{gameObject.name}' is missing MonoEntity on the root. Replication disabled.");
                return;
            }
            var context = _monoEntity;

            // Resolve tick interval up front — AuthorityRenderBinding's coalesce / stale
            // windows are sized relative to it (see ISSUES.md #23). tickRate == 0 is the
            // degenerate path (#16 regression guard); bindings still construct but with
            // tickDelta = 0 the authority-render windows collapse and the interpolated
            // bindings list is cleared below so TickRender never runs.
            uint tickRate = NetworkManager.NetworkTickSystem.TickRate;
            _tickInterval = tickRate > 0 ? 1.0 / tickRate : 0;
            _interpolationDelaySeconds = _interpolationDelayTicks * _tickInterval;

            // Resolve the replication system up-front so the binding loop can inject it
            // into the factory for EntityRef-typed fields (EntityRefCodec needs an
            // IEntityRefResolver reference at construction time). GetOrCreate is
            // idempotent per NetworkManager; Register further down still does the
            // per-replicator hookup.
            _system = AspectReplicationSystem.GetOrCreate(NetworkManager);

            _bindingsScratch.Clear();
            _bindingAuthoritiesScratch.Clear();
            _interpolatedBindingsScratch.Clear();
            _eventBindingsScratch.Clear();
            _predictedFieldsScratch.Clear();
            // Step 7 plumbing: binding index per predicted field. Populated alongside
            // _bindingsScratch so the index we store is the final _bindings[] index.
            _predictedBindingIndicesScratch.Clear();

            // Sort aspects by full type name so the dirty-bitmask index of each field is
            // stable between server and client, independent of the order components call
            // Context.Require<T>() in Awake(). Manual sort avoids LINQ allocations on spawn.
            _aspectListScratch.Clear();
            foreach (var a in context.GetAllAspects()) _aspectListScratch.Add(a);
            _aspectListScratch.Sort((a, b) => string.Compare(
                a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal));
            for (int ai = 0; ai < _aspectListScratch.Count; ai++)
            {
                var aspect = _aspectListScratch[ai];
                // Hoist predicted scan above the field loop so FieldBindingKind resolution knows
                // which server-auth fields the owner writes locally via ISimulate. Those fields
                // need AuthorityRenderBinding even though the owner isn't the replication
                // authority — without this, the owner's .Smooth() would render network-delayed
                // server state instead of the predicted value.
                var predictedInfos = PredictionScanner.Scan(aspect);
                _predictedFieldNamesScratch.Clear();
                if (predictedInfos.Length > 0)
                {
                    for (int pi = 0; pi < predictedInfos.Length; pi++)
                        _predictedFieldNamesScratch.Add(predictedInfos[pi].Field.Name);
                }

                // Track (fieldName -> bindingIndex) for this aspect so we can join
                // PredictionScanner's output back to the exact binding that owns
                // each Predicted field. Scanners both sort by name, but a field
                // that was skipped (null reactive, type mismatch) does not become
                // a binding — the dictionary only holds entries we actually added.
                _aspectBindingByNameScratch.Clear();
                var fieldInfos = ReplicationScanner.Scan(aspect);
                foreach (var info in fieldInfos)
                {
                    var reactive = info.Field.GetValue(aspect);
                    if (reactive == null)
                    {
                        Debug.LogError($"[AspectReplicator] Aspect '{aspect.GetType().Name}' field '{info.Field.Name}' is null on '{gameObject.name}'. Initialize it in the aspect constructor or field initializer.");
                        continue;
                    }
                    bool isAuthority = info.Authority == AuthorityMode.Server ? IsServer : IsOwner;
                    // Collections don't participate in prediction (scanner enforces this),
                    // so predicted-owner evaluation only matters for scalar fields.
                    bool isPredictedOwner = false;
                    FieldBindingKind kind = FieldBindingKind.Plain;
                    if (info.Kind == ReplicatedFieldKind.Scalar)
                    {
                        // Predicted-owner: owner-client of a server-auth Predicted field. They run
                        // ISimulate locally each tick, so their render path needs AuthorityRender
                        // smoothing — but they are NOT the replication authority (server is), so we
                        // don't subscribe them via SubscribeAsAuthority. The !IsServer guard excludes
                        // host-owner (already covered by isAuthority via IsServer=true).
                        isPredictedOwner =
                            info.Authority == AuthorityMode.Server
                            && IsOwner && !IsServer
                            && _predictedFieldNamesScratch.Contains(info.Field.Name);

                        // "Writes locally each tick" is what AuthorityRenderBinding exists for.
                        bool writesLocally = isAuthority || isPredictedOwner;

                        kind = info.Interpolation switch
                        {
                            InterpolationMode.Linear when writesLocally => FieldBindingKind.AuthorityRendered,
                            InterpolationMode.Linear                    => FieldBindingKind.PassiveInterpolated,
                            _                                           => FieldBindingKind.Plain,
                        };
                    }

                    ReplicatedFieldBinding binding = info.Kind switch
                    {
                        ReplicatedFieldKind.ObservableList =>
                            ReplicatedFieldBindingFactory.CreateObservableList(reactive, info.ValueType, info.Quantization, _system),
                        ReplicatedFieldKind.ObservableDictionary =>
                            ReplicatedFieldBindingFactory.CreateObservableDictionary(reactive, info.KeyType!, info.ValueType, info.Quantization, _system),
                        ReplicatedFieldKind.ObservableHashSet =>
                            ReplicatedFieldBindingFactory.CreateObservableHashSet(reactive, info.ValueType, info.Quantization, _system),
                        ReplicatedFieldKind.ObservableRingBuffer =>
                            ReplicatedFieldBindingFactory.CreateObservableRingBuffer(reactive, info.ValueType, info.Quantization, _system),
                        _ =>
                            ReplicatedFieldBindingFactory.Create(reactive, info.ValueType, kind, _tickInterval, info.Quantization, _system),
                    };

                    if (isAuthority)
                    {
                        // Owner-auth subscriptions go into a separate bag so they can be
                        // disposed/re-created on ownership transfer without touching
                        // server-auth subscriptions that live for the entity's full lifetime.
                        ref var bag = ref (info.Authority == AuthorityMode.Owner ? ref _ownerDisposables : ref _disposables);
                        binding.SubscribeAsAuthority(ref bag);
                        // R3 ReactiveProperty.Subscribe replays the current value, so the
                        // callback fires once synthetically with _suppressNotification == false
                        // and flips OwnerWroteSinceSpawn to true before the entity has done any
                        // real work. Without this reset, initial-sync on a late-joining owner
                        // would see the flag already set and skip every server-preset owner-auth
                        // field — the exact failure mode #19 is supposed to close.
                        binding.ResetOwnerWroteSinceSpawn();
                    }
                    else if (isPredictedOwner)
                    {
                        // Predicted-owner subscribe: sample-only, no dirty flag. Lives on
                        // _ownerDisposables so it tears down on OnLostOwnership — a non-owner
                        // peer has no local writes to sample.
                        binding.SubscribeForLocalSampling(ref _ownerDisposables);
                    }

                    _aspectBindingByNameScratch[info.Field.Name] = _bindingsScratch.Count;
                    _bindingsScratch.Add(binding);
                    _bindingAuthoritiesScratch.Add(info.Authority);
                    if (binding.IsInterpolated)
                        _interpolatedBindingsScratch.Add(binding);
                }

                // Predicted fields are a subset of replicated fields (same attribute,
                // Predicted = true flag). PredictionScanner filters ReplicationScanner's
                // output, so the only reason a predicted field would not match here is
                // if the replicated binding was skipped (null reactive) — log and drop
                // the predicted entry rather than producing an index that writes garbage
                // on capture.
                for (int pi = 0; pi < predictedInfos.Length; pi++)
                {
                    var predictedInfo = predictedInfos[pi];
                    if (!_aspectBindingByNameScratch.TryGetValue(predictedInfo.Field.Name, out var bindingIndex))
                    {
                        Debug.LogError($"[AspectReplicator] Aspect '{aspect.GetType().Name}' field '{predictedInfo.Field.Name}' has [Replicated(Predicted = true)] but no matching replicated binding was registered (null reactive?). Prediction snapshot will exclude this field.");
                        continue;
                    }
                    _predictedFieldsScratch.Add(predictedInfo);
                    _predictedBindingIndicesScratch.Add(bindingIndex);
                }

                var eventInfos = ReplicationScanner.ScanEvents(aspect);
                foreach (var info in eventInfos)
                {
                    var subject = info.Field.GetValue(aspect);
                    if (subject == null)
                    {
                        Debug.LogError($"[AspectReplicator] Aspect '{aspect.GetType().Name}' field '{info.Field.Name}' is null on '{gameObject.name}'. Initialize it in the aspect constructor or field initializer.");
                        continue;
                    }
                    var binding = ReplicatedEventBindingFactory.Create(subject, info.ValueType, info.Authority, info.Reliability);
                    _eventBindingsScratch.Add(binding);
                }
            }

            _bindings = _bindingsScratch.ToArray();
            _bindingAuthorities = _bindingAuthoritiesScratch.ToArray();
            _eventBindings = _eventBindingsScratch.ToArray();
            _interpolatedBindings = _interpolatedBindingsScratch.ToArray();

            // Regression guard #3 (ISSUES.md): the dirty-mask bit and every event-id on the
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
            // degenerate tick rate (see #16), Update's TickRender loop must still bail —
            // drop the interpolated list now that the loop has flattened it.
            if (_tickInterval == 0)
                _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();

            if (ExceedsEventBindingCap(_eventBindings.Length, gameObject.name))
            {
                _system = null;
                return;
            }

            _predictedFields = _predictedFieldsScratch.ToArray();
            _predictedBindingIndices = _predictedBindingIndicesScratch.ToArray();

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

            // Regression guard #4 (ISSUES.md): the cap/clamp and mask-size recompute steps
            // above have to stay coupled — bindings length, authority count, mask byte count,
            // dirty-mask buffer length, and every predicted index must line up. They do today
            // by construction, but a future reorder or new clamp could desync them silently.
            // Debug.Assert is compiled out of release builds, so this costs nothing shipped.
            Debug.Assert(_bindings.Length == _bindingAuthorities.Length,
                "bindings / authorities length drift");
            Debug.Assert(_maskByteCount == (_bindings.Length + 7) / 8,
                "mask byte count does not match binding count");
            Debug.Assert(_dirtyMaskBuffer.Length == _maskByteCount,
                "dirty mask buffer size drift");
            for (int i = 0; i < _predictedBindingIndices.Length; i++)
                Debug.Assert(_predictedBindingIndices[i] < _bindings.Length,
                    "predicted index out of range");
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
        //   network payloads into the wrong reactive properties. See ISSUES.md #3, #18.
        //
        // Both helpers are predicates: they do not mutate the caller's array. On overflow
        // OnNetworkSpawn returns without registering with the replication system, which
        // is strictly safer than silent truncation.

        internal static bool ExceedsFieldBindingCap(int bindingCount, string entityName)
        {
            if (bindingCount > 256)
            {
                Debug.LogError($"[AspectReplicator] Entity '{entityName}' has {bindingCount} replicated fields, max is 256. Aborting spawn — this replicator will not register with the replication system.");
                return true;
            }
            return false;
        }

        internal static bool ExceedsEventBindingCap(int bindingCount, string entityName)
        {
            if (bindingCount > 256)
            {
                Debug.LogError($"[AspectReplicator] Entity '{entityName}' has {bindingCount} replicated events, max is 256. Aborting spawn — this replicator will not register with the replication system.");
                return true;
            }
            return false;
        }

        // Extracted for unit-testing: the live ApplyOwnerSubmission needs a NetworkManager
        // and a real FastBufferReader payload, but the offset-tracking math is independent
        // of both. First sample seeds exactly; later samples feed an EMA so mid-session
        // NGO clock re-syncs converge over a few seconds without single-frame jitter
        // moving receivedTime visibly.
        internal void UpdateOwnerSubmitTickOffset(int serverTick, int senderTick)
        {
            double rawOffset = serverTick - senderTick;
            if (!_hasOwnerSubmitTickOffset)
            {
                _ownerSubmitTickOffset = rawOffset;
                _hasOwnerSubmitTickOffset = true;
                return;
            }
            _ownerSubmitTickOffset = 0.9 * _ownerSubmitTickOffset + 0.1 * rawOffset;
        }

        public override void OnNetworkDespawn()
        {
            if (_predictionHook != null)
            {
                _predictionHook.Unregister(this);
                _predictionHook = null;
            }
            _predictedInputType = null;

            _system?.Unregister(this);
            _system = null;
            _ownerSubmitTickOffset = 0;
            _hasOwnerSubmitTickOffset = false;

            for (int i = 0; i < _bindings.Length; i++)
                _bindings[i].OnDespawn();

            _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
            _ownerDisposables.Dispose();
            _disposables.Dispose();

            // Null the cached MonoEntity AFTER Unregister so EntityId is still readable
            // while the system tears down its _byEntityId index.
            _monoEntity = null;
        }

        private void Update()
        {
            if (_interpolatedBindings.Length == 0) return;
            if (!IsSpawned) return;

            double renderTime = NetworkManager.ServerTime.Time - _interpolationDelaySeconds;
            for (int i = 0; i < _interpolatedBindings.Length; i++)
                _interpolatedBindings[i].TickRender(renderTime);
        }

        public override void OnGainedOwnership()
        {
            // Subscribe owner-auth field and event bindings now that this peer
            // is the authority. Previous owner's subscriptions were disposed in
            // their OnLostOwnership.
            SubscribeOwnerFieldBindings();
            SubscribeEventBindingsAsAuthority();

            // Reset the tick offset — a new owner has a different clock drift.
            _ownerSubmitTickOffset = 0;
            _hasOwnerSubmitTickOffset = false;

            // Clear stale interpolation buffers for owner-auth fields — this peer
            // is now authority and writes locally, so old snapshots are irrelevant.
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindingAuthorities[i] == AuthorityMode.Owner)
                    _bindings[i].ClearInterpolationState();
            }

            ReapplyOwnerScope();
        }

        public override void OnLostOwnership()
        {
            // Tear down owner-auth subscriptions — this peer is no longer the
            // authority for those fields/events.
            _ownerDisposables.Dispose();
            _ownerDisposables = default;

            // Clear owner-auth interpolation state symmetric to OnGainedOwnership.
            // The subscribe-side sampler that fed AuthorityRenderBinding's render
            // pair is gone now; the binding must drop _samplesFromSubscribe so
            // incoming network snapshots (relayed from the new owner) sample
            // through ApplyFromNetwork instead of being silently skipped. Without
            // this, InterpolatedValue freezes on the last local write.
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindingAuthorities[i] == AuthorityMode.Owner)
                    _bindings[i].ClearInterpolationState();
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

        private void SubscribeEventBindingsAsAuthority()
        {
            if (_system == null) return;

            for (int i = 0; i < _eventBindings.Length; i++)
            {
                var binding = _eventBindings[i];
                bool isAuthority = binding.Authority == AuthorityMode.Server ? IsServer : IsOwner;
                if (!isAuthority) continue;

                // Host-owner (IsServer && IsOwner) bypasses the owner->server hop and broadcasts
                // directly to NotServer. Pure client owner submits to server, which relays.
                bool isOwnerSubmit = binding.Authority == AuthorityMode.Owner && !IsServer;
                ref var bag = ref (binding.Authority == AuthorityMode.Owner ? ref _ownerDisposables : ref _disposables);
                binding.SubscribeAsAuthority(ref bag, (byte)i, _system, NetworkObjectId, isOwnerSubmit);
            }
        }

        private void ApplyNetworkScopes()
        {
            var myNetworkObject = NetworkObject;
            _scopeComponentsBuffer.Clear();
            GetComponentsInChildren(includeInactive: true, _scopeComponentsBuffer);
            _ownerScopedScratch.Clear();

            for (int i = 0; i < _scopeComponentsBuffer.Count; i++)
            {
                var component = _scopeComponentsBuffer[i];

                // Skip any IEntityComponent that is not a Behaviour (pure C# or otherwise).
                if (component is not Behaviour behaviour) continue;

                // Stop at nested NetworkObject boundaries. Children that belong to a
                // different NetworkObject must not be scope-managed by this replicator.
                if (behaviour.GetComponentInParent<NetworkObject>() != myNetworkObject) continue;

                var scope = NetworkScopeScanner.GetScope(component.GetType());
                if (scope == NetworkScope.Everywhere) continue;

                if (scope == NetworkScope.ServerOnly)
                {
                    behaviour.enabled = IsServer;
                }
                else // OwnerOnly
                {
                    behaviour.enabled = IsOwner;
                    _ownerScopedScratch.Add(behaviour);
                }
            }

            _ownerScopedComponents = _ownerScopedScratch.Count > 0
                ? _ownerScopedScratch.ToArray()
                : Array.Empty<Behaviour>();
        }

        private void ReapplyOwnerScope()
        {
            for (int i = 0; i < _ownerScopedComponents.Length; i++)
                _ownerScopedComponents[i].enabled = IsOwner;
        }

        // ------------------------------------------------------------------
        // State application — called by AspectReplicationSystem
        // ------------------------------------------------------------------

        /// <summary>
        /// Apply incoming state from a FastBufferReader (named message path).
        /// The reader position is at serverTick (mask + fields follow). The
        /// <paramref name="serverTick"/> out parameter is surfaced so the caller
        /// (AspectReplicationSystem) can route it to the prediction reconcile
        /// hook without re-parsing the wire payload.
        /// </summary>
        internal unsafe void ApplyStateBuffer(FastBufferReader reader, StateApplyMode mode, out int serverTick)
        {
            reader.ReadValueSafe(out serverTick);
            var mask = stackalloc byte[_maskByteCount];
            reader.ReadBytesSafe(mask, _maskByteCount);
            double receivedTime = serverTick * _tickInterval;

            for (int i = 0; i < _bindings.Length; i++)
            {
                if ((mask[i >> 3] & (1 << (i & 7))) == 0) continue;

                // Server-auth fields always apply. Owner-auth fields apply or skip
                // depending on mode — the short-circuit on authority keeps server-auth
                // fields out of the decision entirely.
                bool skip = _bindingAuthorities[i] == AuthorityMode.Owner && mode switch
                {
                    StateApplyMode.SkipOwnerAuth => true,
                    StateApplyMode.SkipOwnerAuthIfLocallyWritten => _bindings[i].OwnerWroteSinceSpawn,
                    _ => false,
                };

                if (skip)
                {
                    _bindings[i].Skip(reader);
                    continue;
                }

                _bindings[i].ReadFrom(reader);
                _bindings[i].ApplyFromNetwork(receivedTime);
            }
        }

        /// <summary>
        /// Server-side: apply owner-submitted state, validate authority, re-mark dirty for relay.
        /// </summary>
        internal unsafe void ApplyOwnerSubmission(FastBufferReader reader, int senderTick)
        {
            int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
            UpdateOwnerSubmitTickOffset(serverTick, senderTick);

            double receivedTime = (senderTick + _ownerSubmitTickOffset) * _tickInterval;
            var mask = stackalloc byte[_maskByteCount];
            reader.ReadBytesSafe(mask, _maskByteCount);

            for (int i = 0; i < _bindings.Length; i++)
            {
                if ((mask[i >> 3] & (1 << (i & 7))) == 0) continue;

                if (_bindingAuthorities[i] != AuthorityMode.Owner)
                {
                    // Owner tried to write a server-auth field — reject but keep the reader aligned.
                    Debug.LogWarning($"[AspectReplicator] Owner submitted server-auth field index {i} on '{gameObject.name}'. Dropping.");
                    _bindings[i].Skip(reader);
                    continue;
                }

                _bindings[i].ReadFrom(reader);
                _bindings[i].ApplyFromNetwork(receivedTime);
                // Re-mark dirty so the next ServerTick relays to other clients.
                _bindings[i].MarkDirty();
            }
        }

        /// <summary>
        /// Build a full-snapshot payload for initial sync (server-side).
        /// Writes serverTick + full mask + all field values into the provided writer.
        /// </summary>
        internal unsafe void BuildInitialSyncPayload(FastBufferWriter writer)
        {
            if (_bindings.Length == 0) return;

            // Full mask: set every bit for every binding.
            for (int j = 0; j < _maskByteCount; j++) _dirtyMaskBuffer[j] = 0xFF;

            int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
            writer.WriteValueSafe(serverTick);
            fixed (byte* maskPtr = _dirtyMaskBuffer)
                writer.WriteBytesSafe(maskPtr, _maskByteCount);

            for (int i = 0; i < _bindings.Length; i++)
                _bindings[i].WriteSnapshotTo(writer);
        }

        // ------------------------------------------------------------------
        // Prediction snapshot capture — used by PredictionManager
        // ------------------------------------------------------------------

        /// <summary>
        /// Serialize current values of every <c>[Replicated(Predicted = true)]</c> field into
        /// <paramref name="slotBuffer"/>. The buffer must be <see cref="PredictedPayloadSize"/>
        /// bytes long; the caller owns it (SnapshotBuffer hands out slices of a
        /// single pre-allocated backing array, so this path is alloc-free after
        /// spawn apart from the Allocator.Temp scratch used to stage the write).
        /// </summary>
        internal unsafe void CapturePredictedState(Span<byte> slotBuffer)
        {
            if (_predictedBindingIndices.Length == 0) return;

            var writer = new FastBufferWriter(slotBuffer.Length, Allocator.Temp, slotBuffer.Length);
            try
            {
                for (int i = 0; i < _predictedBindingIndices.Length; i++)
                    _bindings[_predictedBindingIndices[i]].WriteTo(writer);

                byte* src = writer.GetUnsafePtr();
                int written = writer.Length;
                fixed (byte* dst = slotBuffer)
                    System.Buffer.MemoryCopy(src, dst, slotBuffer.Length, written);
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// Routed by <see cref="AspectReplicationSystem.OnStateBatchReceived"/>
        /// after it has applied an incoming state batch. Fans out to the
        /// prediction manager matching this entity's <c>TInput</c> so it can
        /// replay inputs <c>serverTick + 1 .. currentTick</c>. No-op when the
        /// entity has no predicted fields.
        /// </summary>
        internal void NotifyServerStateApplied(int serverTick)
        {
            _predictionHook?.OnServerStateApplied(this, serverTick);
        }

        // ------------------------------------------------------------------
        // Event dispatch — called by AspectReplicationSystem
        // ------------------------------------------------------------------

        /// <summary>
        /// Client-side: dispatch an incoming event from the server broadcast.
        /// </summary>
        internal void DispatchEvent(byte eventIndex, FastBufferReader reader)
        {
            // Host already fired the Subject locally on the authority side — skip to avoid double-apply.
            if (IsHost) return;

            if (eventIndex >= _eventBindings.Length)
            {
                Debug.LogError($"[AspectReplicator] Event index {eventIndex} out of range ({_eventBindings.Length} bindings) on '{gameObject.name}'.");
                return;
            }

            var binding = _eventBindings[eventIndex];

            // Pure client owner: it is authority for this owner-auth event and has already
            // fired the Subject locally at user-write time. The server relay is a duplicate.
            if (IsOwner && binding.Authority == AuthorityMode.Owner) return;

            binding.ApplyFromNetwork(reader);
        }

        /// <summary>
        /// Server-side: handle an owner-submitted event — validate, relay, and fire locally.
        /// </summary>
        internal void HandleOwnerEvent(byte eventIndex, FastBufferReader reader, IEventBroadcaster broadcaster)
        {
            if (eventIndex >= _eventBindings.Length)
            {
                Debug.LogError($"[AspectReplicator] Owner event index {eventIndex} out of range ({_eventBindings.Length} bindings) on '{gameObject.name}'.");
                return;
            }

            var binding = _eventBindings[eventIndex];
            if (binding.Authority != AuthorityMode.Owner)
            {
                Debug.LogWarning($"[AspectReplicator] Owner submitted server-auth event index {eventIndex} on '{gameObject.name}'. Dropping.");
                return;
            }

            // Read the event payload, relay to clients, and fire locally on the server.
            int payloadSize = binding.PayloadSize;
            unsafe
            {
                byte* temp = stackalloc byte[payloadSize];
                reader.ReadBytesSafe(temp, payloadSize);

                // Build relay message for other clients.
                var relayWriter = new FastBufferWriter(sizeof(ulong) + sizeof(byte) + payloadSize, Allocator.Temp);
                try
                {
                    relayWriter.WriteValueSafe(NetworkObjectId);
                    relayWriter.WriteValueSafe(eventIndex);
                    relayWriter.WriteBytesSafe(temp, payloadSize);

                    broadcaster.SendEvent(NetworkObjectId, eventIndex, relayWriter,
                        binding.Authority, binding.Reliability, isOwnerSubmit: false);
                }
                finally
                {
                    relayWriter.Dispose();
                }

                // Fire locally on the server so server-side listeners see the event.
                // Wrap the stackalloc buffer directly — Allocator.None means no copy,
                // reader does not own the pointer, Dispose is a no-op on the buffer.
                var localReader = new FastBufferReader(temp, Allocator.None, payloadSize);
                try
                {
                    binding.ApplyFromNetwork(localReader);
                }
                finally
                {
                    localReader.Dispose();
                }
            }
        }

        // ------------------------------------------------------------------
        // Prediction bootstrap
        // ------------------------------------------------------------------

        private void BootstrapPrediction()
        {
            // Gate on ISimulate, not on Predicted = true. The pipeline is needed
            // whenever a component wants to run tick-driven authoritative logic
            // fed by owner input — that is independent of whether any field
            // opts into the snapshot+reconcile path. The Predicted flag purely
            // controls the latter (capture + rewind on state arrival); without
            // it the snapshot buffer stays empty-sized and the reconcile call
            // no-ops, but owner/server Simulate still run and inputs still flow.
            //
            // Concretely this lets a prefab flip `Authority = Server` ↔ `Owner`
            // on a replicated field without also having to toggle `Predicted`:
            //   - Server-auth + Predicted: full prediction + reconcile
            //   - Server-auth, no Predicted: owner sends input → server
            //     Simulates → broadcasts; owner's local Simulate visibly
            //     snaps back each broadcast (textbook "no prediction" feel)
            //   - Owner-auth (Predicted is a no-op, stripped by the scanner):
            //     owner's Simulate writes are the authoritative ones, relayed
            //     via the owner-auth path; the server-side Simulate pass
            //     writes to a non-authoritative local copy that gets
            //     overwritten by the owner relay
            _predictedInputType = ResolveInputType();
            if (_predictedInputType == null)
            {
                // Predicted flag without an ISimulate consumer is a programmer
                // error — the field is dressed up for prediction but nothing
                // will ever run against it. Call it out so it's easy to spot.
                if (_predictedFields.Length > 0)
                    Debug.LogError($"[AspectReplicator] '{gameObject.name}' has Predicted fields but no component implements ISimulate<TInput>. Prediction disabled for this entity.");
                return;
            }

            // GetOrCreate can refuse to build a manager — today only when TickRate is 0
            // (see PredictionManager.GetOrCreate + ISSUES.md #16). On refusal this
            // replicator's predicted fields go unsubscribed, which matches the rest
            // of the early-return contract in OnNetworkSpawn.
            var hook = PredictionHookCache.Resolve(_predictedInputType, NetworkManager);
            if (hook == null)
            {
                _predictedInputType = null;
                return;
            }
            _predictionHook = hook;
            hook.Register(this);
        }

        // Find the first ISimulate<T> implemented by any MonoBehaviour under this
        // NetworkObject (stopping at nested NetworkObject boundaries, same rule
        // ApplyNetworkScopes uses). The returned generic argument is the TInput this
        // entity's prediction pipeline runs on.
        //
        // Cached by NetworkObject.PrefabIdHash (stable per-prefab) so a spawn wave of
        // identical prefabs pays the reflection walk once. See ISSUES.md #12.
        // PrefabIdHash is 0 for non-prefab (scene-placed) NetworkObjects — we skip the
        // cache in that case so unrelated scene objects don't collide on key 0.
        private Type? ResolveInputType()
        {
            var myNetworkObject = NetworkObject;
            uint prefabHash = myNetworkObject.PrefabIdHash;
            bool useCache = prefabHash != 0;
            if (useCache && s_InputTypeCache.TryGetValue(prefabHash, out var cached))
                return cached;

            _behavioursScratch.Clear();
            GetComponentsInChildren(includeInactive: true, _behavioursScratch);

            Type? resolved = null;
            for (int i = 0; i < _behavioursScratch.Count; i++)
            {
                var behaviour = _behavioursScratch[i];
                if (behaviour == null) continue;
                // Walk up the transform chain to the closest NetworkObject — same
                // rule GetComponentInParent<NetworkObject>() enforces, but alloc-free
                // (TryGetComponent does not allocate, whereas GetComponentInParent
                // walks and materializes an array on each call).
                if (!BelongsToNetworkObject(behaviour.transform, myNetworkObject)) continue;

                var interfaces = behaviour.GetType().GetInterfaces();
                for (int j = 0; j < interfaces.Length; j++)
                {
                    var iface = interfaces[j];
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ISimulate<>))
                    {
                        resolved = iface.GetGenericArguments()[0];
                        break;
                    }
                }
                if (resolved != null) break;
            }

            if (useCache) s_InputTypeCache[prefabHash] = resolved;
            return resolved;
        }

        // Walks up the transform chain and returns true iff the first NetworkObject
        // encountered is the same instance as `target`. Mirrors the semantics of
        // `behaviour.GetComponentInParent<NetworkObject>() == target` without that
        // method's per-call allocation.
        private static bool BelongsToNetworkObject(Transform t, NetworkObject target)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                if (cur.TryGetComponent<NetworkObject>(out var no))
                    return no == target;
            }
            return false;
        }

        // Resolves PredictionManager<TInput> instances into a non-generic
        // IAspectPredictionHook view so AspectReplicator (non-generic) can hold
        // a typed reference after one reflective Invoke. Cached MethodInfo keeps
        // the per-Register cost to a single Invoke + single object[]. All
        // subsequent Register / Unregister / OnServerStateApplied calls flow
        // through a direct virtual call on the stored interface reference —
        // zero reflection, zero allocation on the reconcile hot path.
        private static class PredictionHookCache
        {
            private static readonly Dictionary<Type, MethodInfo> s_GetOrCreate = new();

            // Play-Mode-without-Domain-Reload safety (ISSUES.md #17 / TODO.md Batch 8).
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStatics()
            {
                s_GetOrCreate.Clear();
            }

            public static IAspectPredictionHook? Resolve(Type tInput, NetworkManager nm)
            {
                if (!s_GetOrCreate.TryGetValue(tInput, out var mi))
                {
                    var managerType = typeof(PredictionManager<>).MakeGenericType(tInput);
                    mi = managerType.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static)
                        ?? throw new InvalidOperationException("PredictionManager.GetOrCreate not found");
                    s_GetOrCreate[tInput] = mi;
                }

                // GetOrCreate returns null when TickRate == 0 (ISSUES.md #16). Cast
                // through the shared interface — PredictionManager<TInput> implements
                // IAspectPredictionHook so this is a plain reference conversion.
                return (IAspectPredictionHook?)mi.Invoke(null, new object?[] { nm });
            }
        }
    }
}
