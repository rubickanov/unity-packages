using System;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Public debug hooks for the replication system. Null by default — zero runtime
    /// overhead unless a subscriber is attached. Intended for editor profiling,
    /// experiments, and ad-hoc bandwidth measurement.
    /// </summary>
    public static class ReplicationDebug
    {
        /// <summary>
        /// Raised on the server immediately after each per-entity payload has been written
        /// to the state batch. Arguments: <c>NetworkObjectId</c> and the number of bytes
        /// written for that entity's body (serverTick + mask + all dirty field values).
        /// Does NOT include the 8-byte <c>NetworkObjectId</c> or the 2-byte <c>payloadBytes</c>
        /// prefix — those are per-entity framing, not per-entity body.
        /// </summary>
        public static Action<ulong, int> OnEntityPayloadWritten;
    }
}
