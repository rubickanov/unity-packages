using System;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Hook point for DI frameworks. Call <see cref="SetInjector"/> with a delegate that
    /// injects dependencies into the given <c>GameObject</c> (e.g. VContainer's
    /// <c>LifetimeScope.InjectGameObject</c>). <see cref="EntityComponent"/> and any
    /// integration in extension packages invoke <see cref="Invoke"/> during their
    /// <c>Awake</c> before aspect injection.
    /// <para/>
    /// Replacing an already-set injector with a different delegate logs a warning —
    /// usually a sign that two DI containers are competing over the same scene. Use
    /// <see cref="ClearInjector"/> to reset state in tests or between sessions.
    /// </summary>
    public static class EntityInjector
    {
        private static Action<GameObject>? _inject;

        // Reset the static injector hook at the start of every play session. With Domain Reload
        // disabled in Project Settings → Enter Play Mode, a delegate set in the previous session
        // survives — usually capturing a LifetimeScope/container that is already destroyed, so
        // the first Invoke would either throw NRE or inject into a dead scope. Mirrors
        // MonoEntity.ResetStaticEvents / MonoWorld.ResetStaticsOnPlayStart.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart() => _inject = null;

        /// <summary>
        /// Registers <paramref name="inject"/> as the DI hook. Setting the same
        /// delegate twice is a no-op. Replacing with a different delegate logs a
        /// warning and overwrites — two competing DI containers is never what the
        /// caller wants, but hot-reload workflows rely on silent overwrite for the
        /// same-delegate case.
        /// </summary>
        public static void SetInjector(Action<GameObject> inject)
        {
            if (inject == null) throw new ArgumentNullException(nameof(inject));
            if (_inject != null && _inject != inject)
            {
                Debug.LogWarning(
                    "EntityInjector: overwriting an existing injector with a different delegate. " +
                    "This usually means two DI containers are competing. Call ClearInjector() first " +
                    "if the overwrite is intentional.");
            }
            _inject = inject;
        }

        /// <summary>
        /// Resets the DI hook. Invocations of <see cref="Invoke"/> become no-ops
        /// until the next <see cref="SetInjector"/>.
        /// </summary>
        public static void ClearInjector() => _inject = null;

        /// <summary>
        /// Invokes the registered injector on <paramref name="gameObject"/>.
        /// No-op if no injector has been set — aspect injection still works.
        /// </summary>
        public static void Invoke(GameObject gameObject) => _inject?.Invoke(gameObject);
    }
}
