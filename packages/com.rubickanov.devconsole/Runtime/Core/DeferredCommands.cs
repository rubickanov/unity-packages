using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// What is left of a <c>;</c> chain or an exec file after <c>wait N</c>: it runs through the same registry, logged
    /// like typed input, once N frames have passed. A frame counts at the start of Update, ticked by
    /// <see cref="ConsoleLoop"/>.
    /// </summary>
    internal static class DeferredCommands
    {
        private struct Entry
        {
            public CommandRegistry Registry;
            public string CommandLine;
            public int FramesLeft;
        }

        private static readonly List<Entry> Pending = new();
        private static readonly List<Entry> Due = new();

        /// <summary>How many chains are waiting.</summary>
        internal static int Count => Pending.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Clear();

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
    }
}
