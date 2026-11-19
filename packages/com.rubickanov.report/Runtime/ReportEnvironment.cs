namespace Rubickanov.Report
{
    /// <summary>The environment events go under in Sentry, and how it is picked for a run.</summary>
    public static class ReportEnvironment
    {
        /// <summary>The Editor and every build before the game ships: Alex's runs and friends' tests.</summary>
        public const string Playtest = "playtest";

        /// <summary>The builds players get once the game ships.</summary>
        public const string Release = "release";

        /// <summary>Everything automated checks send, to be filtered out.</summary>
        public const string Agent = "agent";

        /// <summary>
        /// The first that fits: <paramref name="asked"/> (<c>-report-env</c>), batch mode or an automated run
        /// (<paramref name="automated"/>: a check's own save or party folder) is <see cref="Agent"/>, else the game's own
        /// name (<paramref name="own"/>, <see cref="Playtest"/> when empty).
        /// </summary>
        public static string Pick(string asked, bool batch, bool automated, string own)
        {
            if (!string.IsNullOrWhiteSpace(asked))
            {
                return asked.Trim();
            }

            if (batch || automated)
            {
                return Agent;
            }

            return string.IsNullOrWhiteSpace(own) ? Playtest : own.Trim();
        }
    }
}
