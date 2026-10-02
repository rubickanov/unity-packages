using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// The package's own messages, in one place so they say the same thing the same way. Routine news (commands
    /// registered, autoexec ran) goes to the console only, so a game's log stays quiet on a normal start. Something
    /// wrong goes to Unity's log with a <c>[DevConsole]</c> prefix, and from there into the console too.
    /// </summary>
    internal static class ConsoleDiagnostics
    {
        private const string Prefix = "[DevConsole] ";

        /// <summary>Routine news: the console's log only.</summary>
        internal static void Info(string message) => ConsoleLog.Log(message);

        /// <summary>
        /// Said in the player log too, though nothing is wrong: for the <c>-command</c> lines of a build nobody watches,
        /// whose log file is all there is to read.
        /// </summary>
        internal static void Report(string message) => Debug.Log(Prefix + message);

        internal static void Warning(string message) => Debug.LogWarning(Prefix + message);

        internal static void Error(string message) => Debug.LogError(Prefix + message);
    }
}
