using UnityEngine;

namespace Rubickanov.GAS.Unity
{
    /// <summary>
    /// Clears <see cref="GasDiagnostics"/> subscribers at the start of every play session.
    /// <para/>
    /// With Domain Reload disabled in Project Settings → Enter Play Mode, statics survive
    /// between sessions: a handler registered by last session's bootstrap stays subscribed
    /// and closes over objects that no longer exist. Mirrors
    /// <c>MonoEntity.ResetStaticEvents</c> in ACS.
    /// <para/>
    /// Lives in GAS.Unity rather than next to <see cref="GasDiagnostics"/> because
    /// GAS.Runtime is built with <c>noEngineReferences</c> and must stay usable from a
    /// headless host; those hosts call <see cref="GasDiagnostics.ResetSubscribers"/> directly.
    /// </summary>
    internal static class GasDiagnosticsResetter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart() => GasDiagnostics.ResetSubscribers();
    }
}
