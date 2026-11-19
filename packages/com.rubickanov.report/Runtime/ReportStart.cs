using System.Collections.Generic;

namespace Rubickanov.Report
{
    /// <summary>What the game gives <see cref="Reporter.Start"/>: who, which build, where the SDK keeps its envelopes.</summary>
    public sealed class ReportStart
    {
        /// <summary>One of <see cref="ReportEnvironment"/>'s names.</summary>
        public string Environment = ReportEnvironment.Playtest;

        /// <summary>The release events and debug files share, such as <c>mygame@0.3.0</c>.</summary>
        public string Release;

        /// <summary>The install's own random id: the only thing about the player that goes.</summary>
        public string UserId;

        /// <summary>The SDK's envelope cache, this install's own (the default is shared by every copy of the game).</summary>
        public string CacheDir;

        /// <summary>An HTTP proxy, <c>host:port</c>, or null; a dead one sends everything to the cache.</summary>
        public string Proxy;

        /// <summary>
        /// Keeps the native crash handler on Linux under Mono, where it is off: breakpad takes the SIGSEGV Mono turns
        /// into a NullReferenceException, so every managed null dereference would end the game. IL2CPP has it on.
        /// </summary>
        public bool LinuxNative;

        /// <summary>Tags on every event and metric from the start.</summary>
        public Dictionary<string, string> Tags = new();

        /// <summary>
        /// Whether reports may start: never while the player has them off, else when <paramref name="asked"/> (a build
        /// always is; the Editor only by a flag, the console or SEND).
        /// </summary>
        public static bool Allowed(bool enabled, bool asked) => enabled && asked;
    }
}
