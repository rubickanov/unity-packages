using System;
using System.Collections.Generic;
using R3;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    [DisallowMultipleComponent]
    public class AspectReplicator : NetworkBehaviour
    {
        private ReplicatedFieldBinding[] _bindings = Array.Empty<ReplicatedFieldBinding>();
        private AuthorityMode[] _bindingAuthorities = Array.Empty<AuthorityMode>();
        private ReplicatedFieldBinding[] _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
        private ReplicatedEventBinding[] _eventBindings = Array.Empty<ReplicatedEventBinding>();
        private Behaviour[] _ownerScopedComponents = Array.Empty<Behaviour>();
        private DisposableBag _disposables;
        private double _tickInterval;
        private double _interpolationDelaySeconds;
        // Fixed-capacity payload size for state RPCs: sizeof(int serverTick) +
        // sizeof(ulong dirtyMask) + sum of each binding's field size (worst case = all dirty).
        // OnOwnerTick does not write serverTick, so this is a loose upper bound there — the
        // 4-byte slack is negligible for Temp allocations.
        private int _statePayloadCap;
        private Action<byte, byte[]> _reliableBroadcaster = null!;
        private Action<byte, byte[]> _unreliableBroadcaster = null!;
        private Action<byte, byte[]> _submitOwnerReliableBroadcaster = null!;
        private Action<byte, byte[]> _submitOwnerUnreliableBroadcaster = null!;

        public override void OnNetworkSpawn()
        {
            // Apply [NetworkScope] first so ServerOnly / OwnerOnly components stop ticking
            // as early as possible on peers where they should not run.
            // Note: NGO does not guarantee OnNetworkSpawn order between NetworkBehaviours
            // on the same NetworkObject — an EntityNetworkComponent's OnNetworkSpawn may
            // already have fired before we disable it. Update() will still be suppressed,
            // and its DisposableBag is released on OnNetworkDespawn.
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
                        binding.SubscribeAsAuthority(ref _disposables);

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

            if (_bindings.Length > 64)
            {
                Debug.LogError($"[AspectReplicator] Entity '{gameObject.name}' has {_bindings.Length} replicated fields, max is 64. Excess fields will be dropped.");
                Array.Resize(ref _bindings, 64);
                Array.Resize(ref _bindingAuthorities, 64);
            }

            // Compute worst-case payload capacity AFTER the clamp so _statePayloadCap reflects
            // exactly the bindings that will actually be written. Must be done before tick-subscribe
            // AND before RequestInitialStateRpc below — the server handler reads this field to size
            // its writer for the initial snapshot reply.
            int payloadCap = sizeof(int) + sizeof(ulong);
            for (int i = 0; i < _bindings.Length; i++)
                payloadCap += _bindings[i].Size;
            _statePayloadCap = payloadCap;

            // Interpolation timing (≈2 ticks behind newest snapshot).
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
            
            

            if (_eventBindings.Length > 256)
            {
                Debug.LogError($"[AspectReplicator] Entity '{gameObject.name}' has {_eventBindings.Length} replicated events, max is 256. Excess events will not be subscribed.");
                Array.Resize(ref _eventBindings, 256);
            }

            // Authority-side event subscriptions. Lambdas (not method groups) keep the Rpc call site direct
            // so NGO's IL post-processor intercepts it cleanly. Cached once per entity, shared across bindings.
            _reliableBroadcaster = BroadcastEventReliableRpc;
            _unreliableBroadcaster = BroadcastEventUnreliableRpc;
            _submitOwnerReliableBroadcaster = SubmitOwnerEventReliableRpc;
            _submitOwnerUnreliableBroadcaster = SubmitOwnerEventUnreliableRpc;
            
            for (int i = 0; i < _eventBindings.Length; i++)
            {
                var binding = _eventBindings[i];
                bool isAuthority = binding.Authority == AuthorityMode.Server ? IsServer : IsOwner;
                if (!isAuthority) continue;

                // Host-owner (IsServer && IsOwner) bypasses the owner→server hop and broadcasts
                // directly to NotServer. Pure client owner submits to server, which relays.
                bool useOwnerSubmit = binding.Authority == AuthorityMode.Owner && !IsServer;
                Action<byte, byte[]> broadcaster = useOwnerSubmit
                    ? (binding.Reliability == Reliability.Reliable ? _submitOwnerReliableBroadcaster : _submitOwnerUnreliableBroadcaster)
                    : (binding.Reliability == Reliability.Reliable ? _reliableBroadcaster : _unreliableBroadcaster);
                binding.SubscribeAsAuthority(ref _disposables, (byte)i, broadcaster);
            }
            

            if (IsServer)
                NetworkManager.NetworkTickSystem.Tick += OnServerTick;

            // Pure client owner broadcasts its own owner-auth state via the owner tick.
            // Host-owner's owner-auth dirty fields are picked up by OnServerTick above.
            if (IsOwner && !IsServer)
                NetworkManager.NetworkTickSystem.Tick += OnOwnerTick;

            // Late-joining clients miss state for fields that never go dirty after spawn
            // (MaxHealth, WeaponId, TeamColor). Pull a full snapshot from the server.
            // Host (IsServer) skip: already has the latest values locally.
            if (!IsServer && _bindings.Length > 0)
                RequestInitialStateRpc();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
                NetworkManager.NetworkTickSystem.Tick -= OnServerTick;

            if (IsOwner && !IsServer)
                NetworkManager.NetworkTickSystem.Tick -= OnOwnerTick;

            _interpolatedBindings = Array.Empty<ReplicatedFieldBinding>();
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

        public override void OnGainedOwnership() => ReapplyOwnerScope();
        public override void OnLostOwnership() => ReapplyOwnerScope();

        private void ApplyNetworkScopes()
        {
            var myNetworkObject = NetworkObject;
            var components = GetComponentsInChildren<IEntityComponent>(includeInactive: true);
            List<Behaviour>? ownerScoped = null;

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];

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

        private void OnServerTick()
        {
            // Build dirty mask across all fields. Owner-auth fields relayed from clients
            // are marked dirty inside SubmitOwnerStateRpc and get picked up here as well.
            ulong dirtyMask = 0;
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindings[i].IsDirty)
                    dirtyMask |= 1UL << i;
            }

            if (dirtyMask == 0) return;

            var writer = new FastBufferWriter(_statePayloadCap, Allocator.Temp);
            try
            {
                // Tag the payload with the current server tick so non-authority clients can
                // stamp interpolation snapshots on a monotonic timeline even when several RPCs
                // arrive in the same frame.
                int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
                writer.WriteValueSafe(serverTick);
                writer.WriteValueSafe(dirtyMask);

                for (int i = 0; i < _bindings.Length; i++)
                {
                    if ((dirtyMask & (1UL << i)) != 0)
                    {
                        _bindings[i].WriteTo(writer);
                        _bindings[i].ClearDirty();
                    }
                }

                BroadcastStateRpc(writer.ToArray());
            }
            finally
            {
                writer.Dispose();
            }
        }

        private void OnOwnerTick()
        {
            // Collect dirty owner-auth fields only. Server-auth fields are not subscribed
            // on the owner side, so they never go dirty here.
            ulong dirtyMask = 0;
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindingAuthorities[i] != AuthorityMode.Owner) continue;
                if (_bindings[i].IsDirty)
                    dirtyMask |= 1UL << i;
            }

            if (dirtyMask == 0) return;

            var writer = new FastBufferWriter(_statePayloadCap, Allocator.Temp);
            try
            {
                writer.WriteValueSafe(dirtyMask);

                for (int i = 0; i < _bindings.Length; i++)
                {
                    if ((dirtyMask & (1UL << i)) != 0)
                    {
                        _bindings[i].WriteTo(writer);
                        _bindings[i].ClearDirty();
                    }
                }

                SubmitOwnerStateRpc(writer.ToArray());
            }
            finally
            {
                writer.Dispose();
            }
        }

        internal void ApplyStateBuffer(byte[] payload, bool skipOwnerFields)
        {
            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                reader.ReadValueSafe(out int serverTick);
                reader.ReadValueSafe(out ulong dirtyMask);
                double receivedTime = serverTick * _tickInterval;

                for (int i = 0; i < _bindings.Length; i++)
                {
                    if ((dirtyMask & (1UL << i)) == 0) continue;

                    if (skipOwnerFields && _bindingAuthorities[i] == AuthorityMode.Owner)
                    {
                        _bindings[i].Skip(reader);
                        continue;
                    }

                    _bindings[i].ReadFrom(reader);
                    _bindings[i].ApplyFromNetwork(receivedTime);
                }
            }
            finally
            {
                reader.Dispose();
            }
        }

        [Rpc(SendTo.NotServer)]
        private void BroadcastStateRpc(byte[] payload)
        {
            // Host is both server and client — it already wrote to aspects directly,
            // so the incoming RPC is redundant. Skip to avoid double-apply.
            if (IsHost) return;

            // Pure client owner: it is authority for owner-auth fields and already has the
            // latest local value. Skipping avoids a relay race where an older owner-written
            // value from the server overwrites a fresher local write.
            ApplyStateBuffer(payload, skipOwnerFields: IsOwner);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestInitialStateRpc(RpcParams rpcParams = default)
        {
            if (_bindings.Length == 0) return;

            // Full mask covering every existing binding. Special-case length==64 because
            // `1UL << 64` is mask-to-63 in C# (== 1), not 0, which would corrupt the mask.
            ulong fullMask = _bindings.Length == 64
                ? ulong.MaxValue
                : (1UL << _bindings.Length) - 1UL;

            var writer = new FastBufferWriter(_statePayloadCap, Allocator.Temp);
            try
            {
                int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
                writer.WriteValueSafe(serverTick);
                writer.WriteValueSafe(fullMask);

                for (int i = 0; i < _bindings.Length; i++)
                    _bindings[i].WriteTo(writer);

                // ToArray() allocates a managed byte[] per late-joining client per entity.
                // Deliberate deferral: fix #6 will eliminate this along with the per-tick allocation.
                SendInitialStateRpc(
                    writer.ToArray(),
                    RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void SendInitialStateRpc(byte[] payload, RpcParams rpcParams = default)
        {
            // IMPORTANT: skipOwnerFields: false here — unlike BroadcastStateRpc.
            // On spawn, the pure-client owner has default(T) locally for owner-auth fields
            // because the ReactiveProperty was just constructed. The server may hold a
            // non-default pre-set value (e.g. WeaponId initialized server-side before
            // ownership transfer) that the owner MUST receive — otherwise the owner stays
            // stuck at default forever. The theoretical downside (owner writes locally
            // between sending the request and receiving the snapshot) is a transient
            // ~ms-scale window; the permanent-default failure mode is strictly worse.
            //
            // TODO: revisit when Owner-auth re-analysis (#12 in ISSUES.md) lands — a
            // per-binding "owner has written locally since spawn" flag would eliminate
            // the residual race cleanly.
            ApplyStateBuffer(payload, skipOwnerFields: false);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitOwnerStateRpc(byte[] payload)
        {
            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                reader.ReadValueSafe(out ulong dirtyMask);

                for (int i = 0; i < _bindings.Length; i++)
                {
                    if ((dirtyMask & (1UL << i)) == 0) continue;

                    if (_bindingAuthorities[i] != AuthorityMode.Owner)
                    {
                        // Owner tried to write a server-auth field — reject but keep the reader aligned.
                        Debug.LogWarning($"[AspectReplicator] Owner submitted server-auth field index {i} on '{gameObject.name}'. Dropping.");
                        _bindings[i].Skip(reader);
                        continue;
                    }

                    _bindings[i].ReadFrom(reader);
                    // Server does not interpolate — it holds truth and relays via OnServerTick.
                    _bindings[i].ApplyFromNetwork(0);
                    // Re-mark dirty so the next OnServerTick relays to other clients via BroadcastStateRpc.
                    _bindings[i].MarkDirty();
                }
            }
            finally
            {
                reader.Dispose();
            }
        }

        [Rpc(SendTo.NotServer)]
        private void BroadcastEventReliableRpc(byte eventIndex, byte[] payload)
        {
            DispatchEvent(eventIndex, payload);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void BroadcastEventUnreliableRpc(byte eventIndex, byte[] payload)
        {
            DispatchEvent(eventIndex, payload);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitOwnerEventReliableRpc(byte eventIndex, byte[] payload)
        {
            HandleOwnerEvent(eventIndex, payload, reliable: true);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitOwnerEventUnreliableRpc(byte eventIndex, byte[] payload)
        {
            HandleOwnerEvent(eventIndex, payload, reliable: false);
        }

        private void HandleOwnerEvent(byte eventIndex, byte[] payload, bool reliable)
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

            // Relay to other clients. On host this also delivers back to DispatchEvent,
            // which skips via the IsHost guard so there is no double-dispatch.
            if (reliable)
                BroadcastEventReliableRpc(eventIndex, payload);
            else
                BroadcastEventUnreliableRpc(eventIndex, payload);

            // Fire locally on the server so server-side listeners see the event. The server
            // is not subscribed as authority for owner-auth events, so this cannot re-serialize.
            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                binding.ApplyFromNetwork(reader);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void DispatchEvent(byte eventIndex, byte[] payload)
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

            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                binding.ApplyFromNetwork(reader);
            }
            finally
            {
                reader.Dispose();
            }
        }
    }
}
