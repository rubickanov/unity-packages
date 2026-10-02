using System.Collections.Generic;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Subscribes to <see cref="ReplicationDebug.OnEntityPayloadWritten"/> and remembers
    /// the last body-size per NetworkObjectId so the HUD can render it next to the entity.
    /// Server-only (event fires on the server that builds the batch). Host counts as server.
    /// Idempotent — first call wires up, subsequent calls are no-ops.
    /// </summary>
    public static class PayloadByteTracker
    {
        private static readonly Dictionary<ulong, int> s_LastPayload = new();
        private static bool s_Wired;

        // With Domain Reload disabled the static subscription and dictionary would survive
        // into the next play session, leaking a dangling handler. Reset on subsystem load.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ReplicationDebug.OnEntityPayloadWritten -= OnPayload;
            s_LastPayload.Clear();
            s_Wired = false;
        }

        public static void EnsureWired()
        {
            if (s_Wired) return;
            ReplicationDebug.OnEntityPayloadWritten += OnPayload;
            s_Wired = true;
        }

        public static bool TryGetLast(ulong networkObjectId, out int bytes)
            => s_LastPayload.TryGetValue(networkObjectId, out bytes);

        private static void OnPayload(ulong id, int bytes) => s_LastPayload[id] = bytes;
    }
}
