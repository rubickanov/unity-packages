using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// The console's start, once per process, on the first frame's Update before any MonoBehaviour.Update: by then
    /// every Awake, OnEnable and Start of the first scene has run, and so have VContainer's IStartable and
    /// IPostStartable of the scopes it built, which run in EarlyUpdate. So commands a game registers anywhere in its
    /// start are there. It discovers the commands, runs <c>autoexec.cfg</c>, then the <c>-command</c> lines of
    /// <see cref="StartupCommands"/> unless the settings turn that off.
    /// </summary>
    internal static class ConsoleStartup
    {
        private static bool _done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => _done = false;

        /// <summary>The player loop's call, every frame; does its work on the first.</summary>
        internal static void Tick()
        {
            if (_done) return;
            Run(CommandRegistry.Instance, DevConsoleSettings.GetOrCreate().RunStartupCommands);
        }

        /// <summary>The start itself, on the first call only. False when it has already run.</summary>
        internal static bool Run(CommandRegistry registry, bool commandLine)
        {
            if (_done) return false;
            _done = true;

            registry.Initialize();
            Commands.ConsoleCommands.RunAutoexec(registry);
            if (commandLine) StartupCommands.Run(registry);
            return true;
        }
    }
}
