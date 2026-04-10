using System;
using System.Collections.Generic;
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
        // Fixed-capacity payload size for state messages: sizeof(int serverTick) +
        // _maskByteCount (variable-length dirty mask) + sum of each binding's field size
        // (worst case = all dirty).
        private int _statePayloadCap;
        private int _maskByteCount;
        private byte[] _dirtyMaskBuffer = Array.Empty<byte>();
        private AspectReplicationSystem? _system;

        // Internal surface exposed to AspectReplicationSystem.
        internal ReplicatedFieldBinding[] Bindings => _bindings;
        internal AuthorityMode[] BindingAuthorities => _bindingAuthorities;
        internal ReplicatedEventBinding[] EventBindings => _eventBindings;
        internal int StatePayloadCap => _statePayloadCap;
        internal int MaskByteCount => _maskByteCount;
        internal byte[] DirtyMaskBuffer => _dirtyMaskBuffer;

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

            // Sort aspects by full type name so the dirty-bitmask index of each field is
            // stable between server and client, independent of the order components call
            // Context.Require<T>() in Awake(). Manual sort avoids LINQ allocations on spawn.
            var aspectList = new List<object>();
            foreach (var a in context.GetAllAspects()) aspectList.Add(a);
            aspectList.Sort((a, b) => string.Compare(
                a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal));
            foreach (var aspect in aspectList)
            {
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
                    // Interpolation runs only on passive consumers: the server always has the raw
                    // value (it writes it or relays it), and the owner of an owner-auth field holds truth locally.
                    bool shouldInterpolate = !IsServer && !isAuthority && info.Interpolation == InterpolationMode.Linear;
                    var binding = ReplicatedFieldBindingFactory.Create(reactive, info.ValueType, shouldInterpolate);

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

                    allBindings.Add(binding);
                    allBindingAuthorities.Add(info.Authority);
                    if (binding.IsInterpolated)
                        allInterpolatedBindings.Add(binding);
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
            _system?.Unregister(this);
            _system = null;

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
        /// The reader position is at serverTick (mask + fields follow).
        /// </summary>
        internal unsafe void ApplyStateBuffer(FastBufferReader reader, StateApplyMode mode)
        {
            reader.ReadValueSafe(out int serverTick);
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
        /// Shim for existing unit tests that pass byte[] payloads.
        /// </summary>
        internal void ApplyStateBuffer(byte[] payload, StateApplyMode mode)
        {
            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                ApplyStateBuffer(reader, mode);
            }
            finally
            {
                reader.Dispose();
            }
        }

        /// <summary>
        /// Server-side: apply owner-submitted state, validate authority, re-mark dirty for relay.
        /// </summary>
        internal unsafe void ApplyOwnerSubmission(FastBufferReader reader)
        {
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
                // Server does not interpolate — it holds truth and relays via ServerTick.
                _bindings[i].ApplyFromNetwork(0);
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
    }
}
