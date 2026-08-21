using System;

namespace Rubickanov.GAS
{
    /// <summary>
    /// Diagnostic sink for non-fatal GAS anomalies (e.g. a modifier aggregation that can't be
    /// evaluated). Left unsubscribed by default so the package stays silent and engine-free —
    /// a host that wants these in its log wires them up once at startup:
    /// <code>
    /// GasDiagnostics.Warning += msg => Debug.LogWarning(msg);
    /// </code>
    /// </summary>
    public static class GasDiagnostics
    {
        /// <summary>
        /// Raised for each diagnostic message. An <c>event</c> rather than a plain delegate
        /// field: consumers may only add and remove their own handler, so one subscriber can
        /// neither wipe another's subscription with an assignment nor raise the callback on
        /// everyone else's behalf.
        /// </summary>
        public static event Action<string>? Warning;

        /// <summary>
        /// Drops every subscriber. Call at the start of a play session when Domain Reload is
        /// disabled (Project Settings → Enter Play Mode) — otherwise handlers registered in a
        /// previous session survive into the next one and fire into dead objects. Unity hosts
        /// get this for free via <c>GasDiagnosticsResetter</c> in the GAS.Unity assembly; this
        /// entry point exists so headless and test hosts can do the same without the engine.
        /// </summary>
        public static void ResetSubscribers() => Warning = null;

        internal static void EmitWarning(string message) => Warning?.Invoke(message);
    }
}
