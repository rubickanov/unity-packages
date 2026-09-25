using System;
using System.Collections.Generic;

namespace Rubickanov.Log
{
    /// <summary>
    /// Channel levels from the command line: <c>-log Course=Verbose,UI=Off</c>, with <c>*</c> for every channel
    /// not named. Channels not mentioned write Info and up.
    /// </summary>
    internal static class LogSettings
    {
        private const string Argument = "-log";
        private const string Everything = "*";

        private static readonly Dictionary<string, LogLevel> Levels = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        public static LogLevel LevelFor(string channel)
        {
            if (!_loaded)
            {
                Load();
            }

            if (Levels.TryGetValue(channel, out LogLevel level) || Levels.TryGetValue(Everything, out level))
            {
                return level;
            }
            return LogLevel.Info;
        }

        public static void Load()
        {
            _loaded = true;
            Levels.Clear();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], Argument, StringComparison.OrdinalIgnoreCase))
                {
                    Parse(args[i + 1], Levels);
                }
            }
        }

        internal static void Parse(string value, IDictionary<string, LogLevel> levels)
        {
            foreach (string pair in value.Split(','))
            {
                string[] parts = pair.Split('=');
                if (parts.Length == 2 && Enum.TryParse(parts[1].Trim(), ignoreCase: true, out LogLevel level))
                {
                    levels[parts[0].Trim()] = level;
                }
            }
        }
    }
}
