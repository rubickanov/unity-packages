using System;
using Unity.Collections;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode
{
    // Prediction-pipeline surface: snapshot capture, reconcile notification, and the
    // one-time bootstrap that resolves TInput + grabs a typed PredictionManager hook.
    // TInput resolution + hook caching lives on PredictionBinder; this partial keeps
    // only the entry points that need access to the replicator's bindings (capture)
    // or fire from its lifecycle (bootstrap / reconcile).
    public partial class EntityReplicator
    {
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
        /// Routed by <see cref="EntityReplicationSystem.OnStateBatchReceived"/>
        /// after it has applied an incoming state batch. Fans out to the
        /// prediction manager matching this entity's <c>TInput</c> so it can
        /// replay inputs <c>serverTick + 1 .. currentTick</c>. No-op when the
        /// entity has no predicted fields.
        /// </summary>
        internal void NotifyServerStateApplied(int serverTick)
        {
            _predictionBinder?.OnServerStateApplied(serverTick);
        }

        // Gate on ISimulate, not on Predicted = true. The pipeline is needed
        // whenever a component wants to run tick-driven authoritative logic
        // fed by owner input — that is independent of whether any field opts
        // into the snapshot+reconcile path. The Predicted flag purely controls
        // the latter (capture + rewind on state arrival); without it the
        // snapshot buffer stays empty-sized and the reconcile call no-ops, but
        // owner/server Simulate still run and inputs still flow.
        //
        // Concretely this lets a prefab flip `Authority = Server` ↔ `Owner`
        // on a replicated field without also having to toggle `Predicted`:
        //   - Server-auth + Predicted: full prediction + reconcile
        //   - Server-auth, no Predicted: owner sends input → server
        //     Simulates → broadcasts; owner's local Simulate visibly
        //     snaps back each broadcast (textbook "no prediction" feel)
        //   - Owner-auth (Predicted is a no-op, stripped by the scanner):
        //     owner's Simulate writes are the authoritative ones, relayed
        //     via the owner-auth path; the server-side Simulate pass writes
        //     to a non-authoritative local copy that gets overwritten by the
        //     owner relay.
        private void BootstrapPrediction()
        {
            _predictionBinder ??= new PredictionBinder(this, NetworkObject);
            _predictionBinder.Bootstrap(_predictedFields.Length, NetworkManager, gameObject.name);
        }
    }
}
