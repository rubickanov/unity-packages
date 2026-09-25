using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// What is left of a <c>;</c> chain or an exec file after <c>wait N</c>: it runs through the same registry, logged
    /// like typed input, once N frames have passed. A frame counts at the start of Update.
    /// </summary>
    internal static class DeferredCommands
    {
        private struct Entry
        {
            public CommandRegistry Registry;
            public string CommandLine;
            public int FramesLeft;
        }

        // The player loop system is found again by this type, so a second registration replaces the first.
        private struct TickSystem { }

        private static readonly List<Entry> Pending = new();
        private static readonly List<Entry> Due = new();

        /// <summary>How many chains are waiting.</summary>
        internal static int Count => Pending.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Clear();
            InsertTickSystem();
        }

        internal static void Clear() => Pending.Clear();

        internal static void Schedule(CommandRegistry registry, string commandLine, int frames)
        {
            Pending.Add(new Entry { Registry = registry, CommandLine = commandLine, FramesLeft = frames });
        }

        /// <summary>Counts one frame and runs the chains whose wait is over, in the order they were scheduled.</summary>
        internal static void Tick()
        {
            if (Pending.Count == 0) return;

            // Taken out before running: a deferred chain may wait again and schedule into Pending
            Due.Clear();
            for (int i = 0; i < Pending.Count; i++)
            {
                var entry = Pending[i];
                entry.FramesLeft--;
                if (entry.FramesLeft <= 0)
                {
                    Due.Add(entry);
                    Pending.RemoveAt(i);
                    i--;
                }
                else
                {
                    Pending[i] = entry;
                }
            }

            for (int i = 0; i < Due.Count; i++)
                Due[i].Registry.ExecuteAndLog(Due[i].CommandLine);
        }

        private static void InsertTickSystem()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            var phases = root.subSystemList;
            if (phases == null) return;

            for (int i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != typeof(UnityEngine.PlayerLoop.Update)) continue;

                var old = phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var systems = new List<PlayerLoopSystem>(old.Length + 1) { new() { type = typeof(TickSystem), updateDelegate = Tick } };
                foreach (var system in old)
                {
                    if (system.type != typeof(TickSystem))
                        systems.Add(system);
                }

                phases[i].subSystemList = systems.ToArray();
                root.subSystemList = phases;
                PlayerLoop.SetPlayerLoop(root);
                return;
            }
        }
    }
}
