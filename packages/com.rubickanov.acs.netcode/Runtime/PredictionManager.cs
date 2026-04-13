using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Per-<see cref="NetworkManager"/> singleton that drives the prediction pipeline
    /// for a single <typeparamref name="TInput"/> type. On each network tick the
    /// locally-owned predicted entities gather input via their <see cref="IInputProvider{TInput}"/>,
    /// submit it to the server via the <c>ACS_Input</c> named message, and run local
    /// prediction through their <see cref="ISimulate{TInput}"/> components. The server
    /// applies the most recently received input per entity and runs the same
    /// <c>ISimulate</c> pass as authority.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Step 6 scaffolding only — there is no snapshot ring buffer or reconciliation
    /// (see <c>DESIGN.md</c> step 7). Predicted clients will visibly snap when the
    /// authoritative replicated state arrives; that is expected until step 7 lands.
    /// </para>
    /// <para>
    /// Subscribes to <c>NetworkTickSystem.Tick</c> <em>before</em>
    /// <see cref="AspectReplicationSystem"/> does — <see cref="AspectReplicator.OnNetworkSpawn"/>
    /// calls <c>PredictionManager.GetOrCreate</c> first so the server's <c>Simulate</c>
    /// writes land as dirty <see cref="ReplicatedFieldBinding"/> marks before the
    /// replication system's <c>ServerTick</c> batches and broadcasts them in the
    /// same frame.
    /// </para>
    /// </remarks>
    [Preserve]
    internal sealed class PredictionManager<TInput> : IDisposable
        where TInput : unmanaged, IInputCommand
    {
        // Include the input type in the channel name so two generic specializations
        // on the same NetworkManager (e.g. gameplay input vs editor debug input)
        // route to their own handlers instead of clobbering each other.
        private static readonly string s_InputChannel = "ACS_Input:" + typeof(TInput).FullName;

        private static readonly Dictionary<NetworkManager, PredictionManager<TInput>> s_Systems = new();

        private sealed class PredictedEntity
        {
            public AspectReplicator Replicator = null!;
            public IInputProvider<TInput>? Provider;
            public ISimulate<TInput>[] Simulators = Array.Empty<ISimulate<TInput>>();
            // Step 6.1: bounded ring of inputs keyed by the tick they were
            // gathered on. Replaces the step-6 single-slot LastInput/HasInput
            // pair so the owner can replay the exact input sequence when a
            // reconcile arrives, and the server can run the tick-aligned input
            // instead of whatever was last received.
            public InputBuffer<TInput> Inputs = InputBuffer<TInput>.Create();
            // Step 7: owner-side snapshots of [Replicated(Predicted = true)] state captured after
            // local Simulate each tick. Null on the server side and on
            // observer-only clients — only allocated when the manager's
            // owner branch first sees this entity. Sized by
            // AspectReplicator.PredictedPayloadSize.
            public SnapshotBuffer? Snapshots;
        }

        private readonly NetworkManager _networkManager;
        private readonly List<PredictedEntity> _entities = new();
        private readonly Dictionary<ulong, PredictedEntity> _byNetworkObjectId = new();
        private readonly float _tickDelta;
        private bool _disposed;
        private bool _warnedMissingProvider;

        private PredictionManager(NetworkManager networkManager)
        {
            _networkManager = networkManager;

            uint tickRate = networkManager.NetworkTickSystem.TickRate;
            _tickDelta = tickRate > 0 ? 1f / tickRate : 0f;

            networkManager.NetworkTickSystem.Tick += OnTick;
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(s_InputChannel, OnInputReceived);

            // If the replication system is already live, push its Tick handler to the
            // tail so our Simulate pass runs first this frame. Otherwise the server's
            // ReactiveProperty writes from Simulate would mark bindings dirty immediately
            // after ServerTick drained them — a full tick of relay latency.
            if (AspectReplicationSystem.TryGet(networkManager, out var replication))
                replication.RequeueTick();
        }

        internal static PredictionManager<TInput> GetOrCreate(NetworkManager networkManager)
        {
            if (!s_Systems.TryGetValue(networkManager, out var system))
            {
                system = new PredictionManager<TInput>(networkManager);
                s_Systems[networkManager] = system;
            }
            return system;
        }

        // Exposed for tests.
        internal static bool TryGet(NetworkManager networkManager, out PredictionManager<TInput> system)
            => s_Systems.TryGetValue(networkManager, out system!);

        internal int EntityCount => _entities.Count;

        internal void Register(AspectReplicator replicator)
        {
            var id = replicator.NetworkObjectId;
            if (_byNetworkObjectId.ContainsKey(id)) return;

            var providers = replicator.GetComponentsInChildren<IInputProvider<TInput>>(includeInactive: true);
            var simulators = replicator.GetComponentsInChildren<ISimulate<TInput>>(includeInactive: true);

            var entity = new PredictedEntity
            {
                Replicator = replicator,
                Provider = providers.Length > 0 ? providers[0] : null,
                Simulators = simulators,
            };

            _entities.Add(entity);
            _byNetworkObjectId[id] = entity;
        }

        internal void Unregister(AspectReplicator replicator)
        {
            var id = replicator.NetworkObjectId;
            if (!_byNetworkObjectId.Remove(id)) return;

            for (int i = _entities.Count - 1; i >= 0; i--)
            {
                if (_entities[i].Replicator == replicator)
                {
                    _entities.RemoveAt(i);
                    break;
                }
            }

            // Mirror AspectReplicationSystem: self-dispose when the last consumer leaves
            // so domain reload / scene teardown doesn't leak a dangling Tick subscription.
            if (_entities.Count == 0)
                Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _networkManager.NetworkTickSystem.Tick -= OnTick;
            _networkManager.CustomMessagingManager?.UnregisterNamedMessageHandler(s_InputChannel);

            _entities.Clear();
            _byNetworkObjectId.Clear();
            s_Systems.Remove(_networkManager);
        }

        // ------------------------------------------------------------------
        // Tick
        // ------------------------------------------------------------------

        private void OnTick()
        {
            if (_disposed) return;

            int tick = _networkManager.NetworkTickSystem.ServerTime.Tick;

            for (int i = 0; i < _entities.Count; i++)
            {
                var entity = _entities[i];
                var rep = entity.Replicator;
                if (rep == null || !rep.IsSpawned) continue;

                bool isServer = rep.IsServer;
                bool isOwner = rep.IsOwner;

                // Owner side: gather locally. Works for both pure-client owner and
                // host-owner — host-owner still needs fresh input for the server
                // Simulate pass below (there is no owner->server hop to fill it in).
                if (isOwner)
                {
                    TInput input = GatherInputFor(entity, rep);
                    entity.Inputs.Store(tick, in input);

                    if (!isServer)
                    {
                        // Pure-client owner: send input to server and run local prediction.
                        SendInput(rep.NetworkObjectId, input);

                        for (int s = 0; s < entity.Simulators.Length; s++)
                            entity.Simulators[s].Simulate(in input, _tickDelta);

                        // Step 7: capture the POST-simulate predicted state for this
                        // tick. Server broadcasts its own post-simulate state tagged with
                        // the same ServerTime.Tick, so reconcile compares apples-to-apples:
                        // owner's predicted(tick) vs server's authoritative(tick).
                        CaptureSnapshot(entity, rep, tick);
                    }
                }

                // Server side: run Simulate as authority. Pick the input tagged with
                // this tick if available; otherwise hold the most recent earlier input
                // (step 6.1 behaviour — same visible outcome as step 6's single-slot
                // hold-last, but now tick-aware so out-of-order ACS_Input arrivals
                // cannot overwrite a newer input with a stale one).
                if (isServer)
                {
                    entity.Inputs.GetOrHoldLast(tick, out TInput input);
                    for (int s = 0; s < entity.Simulators.Length; s++)
                        entity.Simulators[s].Simulate(in input, _tickDelta);
                }
            }
        }

        private static void CaptureSnapshot(PredictedEntity entity, AspectReplicator rep, int tick)
        {
            int payloadSize = rep.PredictedPayloadSize;
            if (payloadSize == 0) return;

            // Lazy-init so observer-only peers that never reach the owner branch
            // don't allocate a snapshot buffer they will never use.
            if (entity.Snapshots == null)
                entity.Snapshots = new SnapshotBuffer(payloadSize);

            var slot = entity.Snapshots.BeginWrite(tick);
            rep.CapturePredictedState(slot);
        }

        private TInput GatherInputFor(PredictedEntity entity, AspectReplicator rep)
        {
            if (entity.Provider != null)
                return entity.Provider.Gather();

            if (!_warnedMissingProvider)
            {
                _warnedMissingProvider = true;
                Debug.LogWarning(
                    $"[PredictionManager<{typeof(TInput).Name}>] Entity '{rep.gameObject.name}' is owned locally " +
                    $"and has predicted fields but no IInputProvider<{typeof(TInput).Name}>. Using default input.");
            }
            return default;
        }

        // ------------------------------------------------------------------
        // Input message I/O
        // ------------------------------------------------------------------

        private unsafe void SendInput(ulong networkObjectId, TInput input)
        {
            int clientTick = _networkManager.NetworkTickSystem.ServerTime.Tick;
            int payloadSize = sizeof(ulong) + sizeof(int) + sizeof(TInput);

            var writer = new FastBufferWriter(payloadSize, Allocator.Temp);
            try
            {
                writer.WriteValueSafe(networkObjectId);
                writer.WriteValueSafe(clientTick);
                byte* ptr = (byte*)&input;
                writer.WriteBytesSafe(ptr, sizeof(TInput));

                _networkManager.CustomMessagingManager.SendNamedMessage(
                    s_InputChannel,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.UnreliableSequenced);
            }
            finally
            {
                writer.Dispose();
            }
        }

        private unsafe void OnInputReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);
            reader.ReadValueSafe(out int senderTick);

            if (!_byNetworkObjectId.TryGetValue(networkObjectId, out var entity))
            {
                // Drain the payload so a future batched-input format stays aligned.
                TInput discard = default;
                reader.ReadBytesSafe((byte*)&discard, sizeof(TInput));
                return;
            }

            // Defense-in-depth: only the owner of the NetworkObject may submit input
            // for it. NGO delivers the senderClientId out-of-band so we can reject
            // forged sends without trusting the payload itself.
            if (entity.Replicator.OwnerClientId != senderClientId)
            {
                Debug.LogWarning(
                    $"[PredictionManager<{typeof(TInput).Name}>] Input for object {networkObjectId} " +
                    $"from non-owner client {senderClientId}. Dropping.");
                TInput discard = default;
                reader.ReadBytesSafe((byte*)&discard, sizeof(TInput));
                return;
            }

            TInput input;
            reader.ReadBytesSafe((byte*)&input, sizeof(TInput));

            entity.Inputs.Store(senderTick, in input);
        }

        // ------------------------------------------------------------------
        // Reconciliation — step 7
        // ------------------------------------------------------------------

        /// <summary>
        /// Called by <see cref="AspectReplicator.NotifyServerStateApplied"/>
        /// immediately after a state batch has written authoritative values
        /// into the predicted fields. Rewinds prediction on the owner client
        /// so the local view re-integrates on top of the authoritative state
        /// instead of snapping back.
        /// </summary>
        /// <remarks>
        /// <para>Runs only on the pure-client owner. The host-owner path skips
        /// reconcile (it IS the authority — nothing to replay; its own
        /// <c>ServerTick</c> writes already hold truth). Observer clients skip
        /// too (no local prediction state to correct).</para>
        /// <para>If the snapshot ring does not cover <paramref name="serverTick"/>
        /// any more — e.g. a severe hitch dropped the owner several seconds
        /// behind the server — we bail. The authoritative state has already
        /// been written to the reactive properties by <c>ApplyStateBuffer</c>
        /// so the view is still correct; we just cannot smooth the transition
        /// via replay.</para>
        /// </remarks>
        internal void OnServerStateApplied(AspectReplicator rep, int serverTick)
        {
            if (_disposed) return;
            if (!_byNetworkObjectId.TryGetValue(rep.NetworkObjectId, out var entity)) return;
            if (entity.Snapshots == null) return; // owner branch never captured — nothing to reconcile
            if (!rep.IsOwner || rep.IsServer) return; // pure-client owner only

            int currentTick = _networkManager.NetworkTickSystem.ServerTime.Tick;
            // Server tick arrived for a tick we have no snapshot of (too old) —
            // or somehow ahead of our local clock. Leave the authoritative value
            // in place, do not replay.
            if (serverTick < entity.Snapshots.OldestTrackedTick) return;
            if (serverTick > currentTick) return;

            // Replay local inputs from serverTick + 1 up to currentTick on top of
            // the authoritative state that ApplyStateBuffer just wrote.
            for (int t = serverTick + 1; t <= currentTick; t++)
            {
                if (!entity.Inputs.TryGet(t, out TInput input)) continue;
                for (int s = 0; s < entity.Simulators.Length; s++)
                    entity.Simulators[s].Simulate(in input, _tickDelta);
            }

            // Refresh the snapshot at currentTick so the next reconcile rewinds
            // to the corrected value, not the pre-reconcile prediction.
            if (rep.PredictedPayloadSize > 0)
            {
                var slot = entity.Snapshots.BeginWrite(currentTick);
                rep.CapturePredictedState(slot);
            }
        }
    }
}
