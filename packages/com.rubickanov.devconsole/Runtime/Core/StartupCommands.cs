using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Console commands handed to the process on its command line: <c>-command "net host 7777"</c>, repeatable and run
    /// in order. It is <c>exec</c> from argv instead of from a file, for a build nobody can type into — a headless
    /// host, a machine driven over ssh, a run that has to start in a known state.
    ///
    /// The game decides WHEN they run, because only the game knows when its own command groups have registered: call
    /// <see cref="Run"/> from a hook that comes after everything has started. The queue drains as it runs and is reset
    /// once per process (subsystem registration, like the rest of this package's statics), so a scene reload that
    /// rebuilds the game's scopes finds nothing left and does not start a second session on top of the first.
    /// </summary>
    public static class StartupCommands
    {
        /// <summary>The argument carrying one console command line; it may appear any number of times.</summary>
        public const string Flag = "-command";

        private static List<string>? _pending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _pending = null;

        /// <summary>The commands this process was started with, in order, that have not run yet.</summary>
        public static IReadOnlyList<string> Pending => _pending ??= new List<string>(Parse(Environment.GetCommandLineArgs()));

        /// <summary>
        /// Picks the command lines out of an argument list: the value following each <see cref="Flag"/>, in the order
        /// they were given. A flag with nothing after it carries no command and is dropped. Pure, so the argument
        /// handling can be tested without starting a process.
        /// </summary>
        public static IReadOnlyList<string> Parse(string[]? args)
        {
            var commands = new List<string>();
            if (args == null)
            {
                return commands;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    break;
                }

                commands.Add(args[i + 1]);
                i++;
            }

            return commands;
        }

        /// <summary>
        /// Runs everything still waiting and empties the queue, returning how many ran. Safe to call again: the second
        /// call finds nothing, which is what keeps a scene reload from starting a second session.
        /// </summary>
        public static int Run(CommandRegistry registry)
        {
            var pending = Pending;
            if (pending.Count == 0)
            {
                return 0;
            }

            // Taken before the first command runs: one of them may reload the scene, and whoever asks then must find
            // the queue already empty
            var commands = new List<string>(pending);
            _pending = new List<string>();

            return Run(registry, commands);
        }

        /// <summary>
        /// Runs the given command lines through the registry, in order, and returns how many were run. Each line is
        /// logged the way <c>exec</c> logs a file's, and the outcome goes to the player log as well: a headless build
        /// has no console window to read, and from there an unregistered command looks exactly like a typo.
        /// </summary>
        public static int Run(CommandRegistry registry, IReadOnlyList<string> commands)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            for (int i = 0; i < commands.Count; i++)
            {
                string command = commands[i];
                ConsoleLog.LogInput(command);

                var result = registry.Execute(command);
                if (result.Success)
                {
                    if (!string.IsNullOrEmpty(result.Message))
                    {
                        ConsoleLog.Log(result.Message!);
                    }

                    Debug.Log($"[DevConsole] {Flag} \"{command}\": {result.Message}");
                }
                else
                {
                    ConsoleLog.LogError(result.Message ?? "failed");
                    Debug.LogError($"[DevConsole] {Flag} \"{command}\" failed: {result.Message}");
                }
            }

            return commands.Count;
        }
    }
}
