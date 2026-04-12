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
        private ReplicatedFieldBinding[] _bindings = Array.Empty<ReplicatedFieldBinding>();
        private AuthorityMode[] _bindingAuthorities = Array.Empty<AuthorityMode>();
        private ReplicatedFieldBinding[] _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
        private ReplicatedEventBinding[] _eventBindings = Array.Empty<ReplicatedEventBinding>();
        private Behaviour[] _ownerScopedComponents = Array.Empty<Behaviour>();
        private readonly List<IEntityComponent> _scopeComponentsBuffer = new();
        private DisposableBag _disposables;
        private DisposableBag _ownerDisposables;
        private double _tickInterval;
        private double _interpolationDelaySeconds;
        // Offset applied to incoming owner-auth senderTick to convert from the client's
        // estimated ServerTime to the server's authoritative time base. Computed once on
        // first owner submission: offset = serverTick - senderTick. This preserves the
        // even spacing of client ticks (+1 per tick) while anchoring them to the server's
        // time reference, preventing interpolation jitter from client clock drift.
        private int _ownerSubmitTickOffset = int.MinValue;
        // Fixed-capacity payload size for state messages: sizeof(int serverTick) +
        // _maskByteCount (variable-length dirty mask) + sum of each binding's field size
        // (worst case = all dirty).
        private int _statePayloadCap;
        private int _maskByteCount;
        private byte[] _dirtyMaskBuffer = Array.Empty<byte>();
        private AspectReplicationSystem? _system;

        // Prediction bookkeeping. Captured at spawn so OnNetworkDespawn can route
        // the unregister call to the right PredictionManager<TInput> instance even
        // though AspectReplicator itself is not generic. Null when no predicted
        // fields exist on this entity.
        private PredictedFieldInfo[] _predictedFields = Array.Empty<PredictedFieldInfo>();
        private Type? _predictedInputType;
        // Indices into _bindings that correspond to [Predicted] fields. Built in
        // OnNetworkSpawn by joining PredictionScanner output with ReplicationScanner
        // output on field name. Drives step 7's snapshot capture/restore path.
        private int[] _predictedBindingIndices = Array.Empty<int>();
        // Σ _bindings[_predictedBindingIndices[i]].Size. Sizes the per-entity
        // SnapshotBuffer slots in PredictionManager.
        private int _predictedPayloadSize;

        // Internal surface exposed to AspectReplicationSystem.
        internal ReplicatedFieldBinding[] Bindings => _bindings;
        internal AuthorityMode[] BindingAuthorities => _bindingAuthorities;
        internal ReplicatedEventBinding[] EventBindings => _eventBindings;
        internal int StatePayloadCap => _statePayloadCap;
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

            var context = GetComponent<EntityContext>();
            if (context == null)
            {
                Debug.LogError($"[AspectReplicator] '{gameObject.name}' is missing EntityContext on the root. Replication disabled.");
                return;
            }

            var allBindings = new List<ReplicatedFieldBinding>();
            var allBindingAuthorities = new List<AuthorityMode>();
            var allInterpolatedBindings = new List<ReplicatedFieldBinding>();
            var allEventBindings = new List<ReplicatedEventBinding>();
            var allPredictedFields = new List<PredictedFieldInfo>();
            // Step 7 plumbing: binding index per predicted field. Populated alongside
            // allBindings so the index we store is the final _bindings[] index.
            var allPredictedBindingIndices = new List<int>();

            // Sort aspects by full type name so the dirty-bitmask index of each field is
            // stable between server and client, independent of the order components call
            // Context.Require<T>() in Awake(). Manual sort avoids LINQ allocations on spawn.
            var aspectList = new List<object>();
            foreach (var a in context.GetAllAspects()) aspectList.Add(a);
            aspectList.Sort((a, b) => string.Compare(
                a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal));
            foreach (var aspect in aspectList)
            {
                // Hoist predicted scan above the field loop so FieldBindingKind resolution knows
                // which server-auth fields the owner writes locally via ISimulate. Those fields
                // need AuthorityRenderBinding even though the owner isn't the replication
                // authority — without this, the owner's .Smooth() would render network-delayed
                // server state instead of the predicted value.
                var predictedInfos = PredictionScanner.Scan(aspect);
                HashSet<string>? predictedFieldNames = null;
                if (predictedInfos.Length > 0)
                {
                    predictedFieldNames = new HashSet<string>(predictedInfos.Length);
                    for (int pi = 0; pi < predictedInfos.Length; pi++)
                        predictedFieldNames.Add(predictedInfos[pi].Field.Name);
                }

                // Track (fieldName -> bindingIndex) for this aspect so we can join
                // PredictionScanner's output back to the exact binding that owns
                // each [Predicted] field. Scanners both sort by name, but a field
                // that was skipped (null reactive, type mismatch) does not become
                // a binding — the dictionary only holds entries we actually added.
                var aspectBindingByName = new Dictionary<string, int>();
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
                    // Predicted-owner: owner-client of a server-auth [Predicted] field. They run
                    // ISimulate locally each tick, so their render path needs AuthorityRender
                    // smoothing — but they are NOT the replication authority (server is), so we
                    // don't subscribe them via SubscribeAsAuthority. The !IsServer guard excludes
                    // host-owner (already covered by isAuthority via IsServer=true).
                    bool isPredictedOwner =
                        info.Authority == AuthorityMode.Server
                        && IsOwner && !IsServer
                        && predictedFieldNames != null
                        && predictedFieldNames.Contains(info.Field.Name);

                    // "Writes locally each tick" is what AuthorityRenderBinding exists for.
                    bool writesLocally = isAuthority || isPredictedOwner;

                    FieldBindingKind kind = info.Interpolation switch
                    {
                        InterpolationMode.Linear when writesLocally => FieldBindingKind.AuthorityRendered,
                        InterpolationMode.Linear                    => FieldBindingKind.PassiveInterpolated,
                        _                                           => FieldBindingKind.Plain,
                    };

                    var binding = ReplicatedFieldBindingFactory.Create(reactive, info.ValueType, kind);

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

                    aspectBindingByName[info.Field.Name] = allBindings.Count;
                    allBindings.Add(binding);
                    allBindingAuthorities.Add(info.Authority);
                    if (binding.IsInterpolated)
                        allInterpolatedBindings.Add(binding);
                }

                // Predicted fields are collected alongside replicated fields so step 7's
                // snapshot buffer can address them without re-scanning. Each predicted
                // field must also be [ReplicatedState] — the attribute contract — so we
                // resolve it to a binding by name. Missing matches mean user error (or a
                // skipped binding due to null reactive); we log and skip that predicted
                // field rather than producing an index that writes garbage on capture.
                for (int pi = 0; pi < predictedInfos.Length; pi++)
                {
                    var predictedInfo = predictedInfos[pi];
                    if (!aspectBindingByName.TryGetValue(predictedInfo.Field.Name, out var bindingIndex))
                    {
                        Debug.LogError($"[AspectReplicator] Aspect '{aspect.GetType().Name}' field '{predictedInfo.Field.Name}' has [Predicted] but no matching [ReplicatedState] binding was registered. Prediction snapshot will exclude this field.");
                        continue;
                    }
                    allPredictedFields.Add(predictedInfo);
                    allPredictedBindingIndices.Add(bindingIndex);
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
                    allEventBindings.Add(binding);
                }
            }

            _bindings = allBindings.ToArray();
            _bindingAuthorities = allBindingAuthorities.ToArray();
            _eventBindings = allEventBindings.ToArray();
            _interpolatedBindings = allInterpolatedBindings.ToArray();

            if (_bindings.Length > 256)
            {
                Debug.LogError($"[AspectReplicator] Entity '{gameObject.name}' has {_bindings.Length} replicated fields, max is 256. Excess fields will be dropped.");
                Array.Resize(ref _bindings, 256);
                Array.Resize(ref _bindingAuthorities, 256);
            }

            // Compute worst-case payload capacity AFTER the clamp so _statePayloadCap reflects
            // exactly the bindings that will actually be written.
            _maskByteCount = (_bindings.Length + 7) / 8;
            _dirtyMaskBuffer = new byte[_maskByteCount];
            int payloadCap = sizeof(int) + _maskByteCount;
            for (int i = 0; i < _bindings.Length; i++)
                payloadCap += _bindings[i].Size;
            _statePayloadCap = payloadCap;

            // Interpolation timing (~2 ticks behind newest snapshot).
            uint tickRate = NetworkManager.NetworkTickSystem.TickRate;
            if (tickRate > 0)
            {
                _tickInterval = 1.0 / tickRate;
                _interpolationDelaySeconds = 2.0 * _tickInterval;
            }
            else
            {
                _tickInterval = 0;
                _interpolationDelaySeconds = 0;
                _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
            }

            EnforceEventBindingCap(ref _eventBindings, gameObject.name);

            _predictedFields = allPredictedFields.ToArray();

            // After the 256-binding clamp above, any predicted-index that points
            // past _bindings.Length is an orphan — its backing binding was dropped
            // by Array.Resize. Drop those indices too so CapturePredictedState
            // never dereferences a non-existent binding, and recompute the payload
            // size from the surviving indices.
            int bindingLimit = _bindings.Length;
            var survivingPredicted = new List<int>(allPredictedBindingIndices.Count);
            for (int i = 0; i < allPredictedBindingIndices.Count; i++)
            {
                if (allPredictedBindingIndices[i] < bindingLimit)
                    survivingPredicted.Add(allPredictedBindingIndices[i]);
            }
            _predictedBindingIndices = survivingPredicted.ToArray();

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

            // Register with the centralized replication system.
            _system = AspectReplicationSystem.GetOrCreate(NetworkManager);
            _system.Register(this);

            // Subscribe event bindings that this peer is authority for.
            SubscribeEventBindingsAsAuthority();

            // Late-joining clients miss state for fields that never go dirty after spawn
            // (MaxHealth, WeaponId, TeamColor). Pull a full snapshot from the server.
            // Host (IsServer) skip: already has the latest values locally.
            if (!IsServer && _bindings.Length > 0)
                _system.RequestInitialSync(this);
        }

        // Extracted to keep the cap invariant unit-testable without spinning up a NetworkManager.
        // Why the cap is exactly 256: event indices are packed into a byte on the wire (see the
        // (byte)i cast in the SubscribeAsAuthority loop above). Without this trim, binding #256
        // wraps to byte 0 and collides with the first event — peers would silently route event
        // payloads to the wrong subject. Regression guard: ISSUES.md #18.
        internal static void EnforceEventBindingCap(ref ReplicatedEventBinding[] bindings, string entityName)
        {
            if (bindings.Length > 256)
            {
                Debug.LogError($"[AspectReplicator] Entity '{entityName}' has {bindings.Length} replicated events, max is 256. Excess events will not be subscribed.");
                Array.Resize(ref bindings, 256);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_predictedInputType != null)
            {
                PredictionHookCache.Unregister(_predictedInputType, NetworkManager, this);
                _predictedInputType = null;
            }

            _system?.Unregister(this);
            _system = null;
            _ownerSubmitTickOffset = int.MinValue;

            for (int i = 0; i < _bindings.Length; i++)
                _bindings[i].OnDespawn();

            _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
            _ownerDisposables.Dispose();
            _disposables.Dispose();
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
            _ownerSubmitTickOffset = int.MinValue;

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
            List<Behaviour>? ownerScoped = null;

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
                    (ownerScoped ??= new List<Behaviour>()).Add(behaviour);
                }
            }

            _ownerScopedComponents = ownerScoped?.ToArray() ?? Array.Empty<Behaviour>();
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
        /// Shim for existing unit tests that pass byte[] payloads and do not
        /// need the server tick.
        /// </summary>
        internal void ApplyStateBuffer(byte[] payload, StateApplyMode mode)
        {
            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                ApplyStateBuffer(reader, mode, out _);
            }
            finally
            {
                reader.Dispose();
            }
        }

        /// <summary>
        /// Server-side: apply owner-submitted state, validate authority, re-mark dirty for relay.
        /// </summary>
        internal unsafe void ApplyOwnerSubmission(FastBufferReader reader, int senderTick)
        {
            int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
            if (_ownerSubmitTickOffset == int.MinValue)
                _ownerSubmitTickOffset = serverTick - senderTick;

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
                _bindings[i].WriteTo(writer);
        }

        // ------------------------------------------------------------------
        // Prediction snapshot capture / restore — used by PredictionManager
        // ------------------------------------------------------------------

        /// <summary>
        /// Serialize current values of every <c>[Predicted]</c> field into
        /// <paramref name="slotBuffer"/>. The buffer must be <see cref="PredictedPayloadSize"/>
        /// bytes long; the caller owns it (SnapshotBuffer reuses a pre-allocated
        /// byte[] per tick slot so this path is alloc-free after spawn, except
        /// for the Allocator.Temp scratch used to stage the write).
        /// </summary>
        internal unsafe void CapturePredictedState(byte[] slotBuffer)
        {
            if (_predictedBindingIndices.Length == 0) return;

            var writer = new FastBufferWriter(_predictedPayloadSize, Allocator.Temp, _predictedPayloadSize);
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
        /// Restore predicted-field values from a snapshot captured by a prior
        /// <see cref="CapturePredictedState"/>. Used as the first step of the
        /// step-7 reconcile loop: the owner rewinds to the snapshot captured
        /// for the authoritative serverTick, then replays local inputs on top.
        /// <para>
        /// Routes through the same <c>ReadFrom → ApplyFromNetwork →
        /// WriteSuppressed</c> path incoming network state uses, so the owner's
        /// own dirty subscription (inactive for server-auth fields on a pure
        /// client anyway) stays silent. <c>receivedTime = 0</c> — interpolation
        /// does not apply here; predicted bindings live on the authority-style
        /// path with no smoothing.
        /// </para>
        /// </summary>
        internal void RestorePredictedState(byte[] slotBuffer)
        {
            if (_predictedBindingIndices.Length == 0) return;

            var reader = new FastBufferReader(slotBuffer, Allocator.Temp);
            try
            {
                for (int i = 0; i < _predictedBindingIndices.Length; i++)
                {
                    var binding = _bindings[_predictedBindingIndices[i]];
                    binding.ReadFrom(reader);
                    binding.ApplyFromNetwork(receivedTime: 0);
                }
            }
            finally
            {
                reader.Dispose();
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
            if (_predictedInputType == null) return;
            PredictionHookCache.Reconcile(_predictedInputType, NetworkManager, this, serverTick);
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
                var localWriter = new FastBufferWriter(payloadSize, Allocator.Temp);
                try
                {
                    localWriter.WriteBytesSafe(temp, payloadSize);
                    var localReader = new FastBufferReader(localWriter, Allocator.Temp);
                    try
                    {
                        binding.ApplyFromNetwork(localReader);
                    }
                    finally
                    {
                        localReader.Dispose();
                    }
                }
                finally
                {
                    localWriter.Dispose();
                }
            }
        }

        // ------------------------------------------------------------------
        // Prediction bootstrap
        // ------------------------------------------------------------------

        private void BootstrapPrediction()
        {
            // Gate on ISimulate, not on [Predicted]. The pipeline is needed
            // whenever a component wants to run tick-driven authoritative logic
            // fed by owner input — that is independent of whether any field
            // opts into the snapshot+reconcile path. [Predicted] purely controls
            // the latter (capture + rewind on state arrival); without it the
            // snapshot buffer stays empty-sized and the reconcile call no-ops,
            // but owner/server Simulate still run and inputs still flow.
            //
            // Concretely this lets a prefab flip `Authority = Server` ↔ `Owner`
            // on a replicated field without also having to toggle `[Predicted]`:
            //   - Server-auth + [Predicted]: full prediction + reconcile
            //   - Server-auth, no [Predicted]: owner sends input → server
            //     Simulates → broadcasts; owner's local Simulate visibly
            //     snaps back each broadcast (textbook "no prediction" feel)
            //   - Owner-auth (± [Predicted]): owner's Simulate writes are the
            //     authoritative ones, relayed via the owner-auth path; the
            //     server-side Simulate pass writes to a non-authoritative
            //     local copy that gets overwritten by the owner relay
            _predictedInputType = ResolveInputType();
            if (_predictedInputType == null)
            {
                // [Predicted] without an ISimulate consumer is a programmer
                // error — the field is dressed up for prediction but nothing
                // will ever run against it. Call it out so it's easy to spot.
                if (_predictedFields.Length > 0)
                    Debug.LogError($"[AspectReplicator] '{gameObject.name}' has [Predicted] fields but no component implements ISimulate<TInput>. Prediction disabled for this entity.");
                return;
            }

            PredictionHookCache.Register(_predictedInputType, NetworkManager, this);
        }

        // Find the first ISimulate<T> implemented by any MonoBehaviour under this
        // NetworkObject (stopping at nested NetworkObject boundaries, same rule
        // ApplyNetworkScopes uses). The returned generic argument is the TInput this
        // entity's prediction pipeline runs on.
        private Type? ResolveInputType()
        {
            var myNetworkObject = NetworkObject;
            var behaviours = GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (behaviour.GetComponentInParent<NetworkObject>() != myNetworkObject) continue;

                var interfaces = behaviour.GetType().GetInterfaces();
                for (int j = 0; j < interfaces.Length; j++)
                {
                    var iface = interfaces[j];
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ISimulate<>))
                        return iface.GetGenericArguments()[0];
                }
            }
            return null;
        }

        // Reflection hooks into PredictionManager<TInput>. Built once per TInput type
        // across all entities in the process — the delegates close over MethodInfo, so
        // invocation cost is one reflective call per register/unregister (each ~once
        // per entity lifetime). This is acceptable because it is not a hot path; the
        // tick loop is reached through PredictionManager directly with no reflection.
        private static class PredictionHookCache
        {
            private struct Entry
            {
                public Action<NetworkManager, AspectReplicator> Register;
                public Action<NetworkManager, AspectReplicator> Unregister;
                public Action<NetworkManager, AspectReplicator, int> Reconcile;
            }

            private static readonly Dictionary<Type, Entry> s_Cache = new();

            public static void Register(Type tInput, NetworkManager nm, AspectReplicator rep)
            {
                if (!s_Cache.TryGetValue(tInput, out var entry))
                {
                    entry = Build(tInput);
                    s_Cache[tInput] = entry;
                }
                entry.Register(nm, rep);
            }

            public static void Unregister(Type tInput, NetworkManager nm, AspectReplicator rep)
            {
                if (!s_Cache.TryGetValue(tInput, out var entry)) return;
                entry.Unregister(nm, rep);
            }

            public static void Reconcile(Type tInput, NetworkManager nm, AspectReplicator rep, int serverTick)
            {
                // No entry yet means no PredictionManager<TInput> was ever built for this
                // NetworkManager — nothing to reconcile against. This is expected on peers
                // that are not locally-owning any predicted entity.
                if (!s_Cache.TryGetValue(tInput, out var entry)) return;
                entry.Reconcile(nm, rep, serverTick);
            }

            private static Entry Build(Type tInput)
            {
                var managerType = typeof(PredictionManager<>).MakeGenericType(tInput);
                var getOrCreate = managerType.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("PredictionManager.GetOrCreate not found");
                var registerMI = managerType.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("PredictionManager.Register not found");
                var tryGetMI = managerType.GetMethod("TryGet", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("PredictionManager.TryGet not found");
                var unregisterMI = managerType.GetMethod("Unregister", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("PredictionManager.Unregister not found");
                var reconcileMI = managerType.GetMethod("OnServerStateApplied", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("PredictionManager.OnServerStateApplied not found");

                Action<NetworkManager, AspectReplicator> register = (nm, rep) =>
                {
                    var instance = getOrCreate.Invoke(null, new object[] { nm })!;
                    registerMI.Invoke(instance, new object[] { rep });
                };

                Action<NetworkManager, AspectReplicator> unregister = (nm, rep) =>
                {
                    // TryGet has an out parameter — reflection stores it in args[1].
                    var args = new object?[] { nm, null };
                    var exists = (bool)tryGetMI.Invoke(null, args)!;
                    if (!exists) return;
                    unregisterMI.Invoke(args[1], new object[] { rep });
                };

                Action<NetworkManager, AspectReplicator, int> reconcile = (nm, rep, serverTick) =>
                {
                    // Reconcile is called from the state-batch receiver on every replicator
                    // that carries _predictedInputType — including replicators that live on
                    // a peer that never got a PredictionManager (observer-only clients for
                    // this TInput). TryGet short-circuits those with no reflection cost
                    // beyond the dictionary probe already done above.
                    var args = new object?[] { nm, null };
                    var exists = (bool)tryGetMI.Invoke(null, args)!;
                    if (!exists) return;
                    reconcileMI.Invoke(args[1], new object[] { rep, serverTick });
                };

                return new Entry { Register = register, Unregister = unregister, Reconcile = reconcile };
            }
        }
    }
}
