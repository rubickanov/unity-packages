using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// The console's system at the start of the player loop's Update, before any MonoBehaviour.Update: the start on
    /// the first frame, then the chains waiting after <c>wait</c>.
    /// </summary>
    internal static class ConsoleLoop
    {
        // Found again by this type, so a second registration replaces the first.
        private struct UpdateSystem { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install() =>
            PlayerLoopSystems.Insert(typeof(UnityEngine.PlayerLoop.Update), typeof(UpdateSystem), Tick, atStart: true);

        private static void Tick()
        {
            ConsoleStartup.Tick();
            DeferredCommands.Tick();
        }
    }
}
