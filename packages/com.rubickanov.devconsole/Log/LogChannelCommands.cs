using Rubickanov.Log;

namespace Rubickanov.DevConsole.Log
{
    /// <summary>
    /// Seeing and changing the levels of <see cref="LogChannel"/>s while the game runs, as <c>-log Course=Verbose</c>
    /// does at start. Discovered with the other commands when <c>com.rubickanov.log</c> is in the project.
    /// </summary>
    internal static class LogChannelCommands
    {
        /// <summary>The channel name that stands for every channel.</summary>
        internal const string Every = "*";

        [ConsoleCommand("log channels", "List the log channels and their levels", "Logging")]
        public static string? Channels()
        {
            var channels = LogChannel.All;
            if (channels.Count == 0) return "No log channels yet: a channel appears once its code first runs.";

            for (int i = 0; i < channels.Count; i++)
                ConsoleLog.Log($"  {channels[i].Name,-16} {channels[i].Level}");
            return null;
        }

        [ConsoleCommand("log level", "Show or set what a log channel writes, * for every channel", "Logging")]
        [AutoComplete(0, typeof(LogChannelProvider))]
        public static string SetLevel(string channel, LogLevel? level = null)
        {
            if (channel == Every)
            {
                if (level == null) throw new CommandException("Usage: log level * <level>");

                var channels = LogChannel.All;
                for (int i = 0; i < channels.Count; i++)
                    channels[i].Level = level.Value;
                return $"Every channel writes {level.Value}";
            }

            // A channel no code has used yet is made now and keeps the level once its code declares it
            var named = LogChannel.Get(channel);
            if (level == null) return $"{named.Name} writes {named.Level}";

            named.Level = level.Value;
            return $"{named.Name} writes {level.Value}";
        }
    }
}
