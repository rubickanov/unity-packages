namespace Rubickanov.Audio
{
    /// <summary>What one cue did in a service: <see cref="IAudioService.Describe"/>.</summary>
    public readonly struct CueStats
    {
        public readonly SoundCue Cue;
        /// <summary>Starts heard.</summary>
        public readonly int Played;
        /// <summary>Starts dropped by the cue's limits or a full pool.</summary>
        public readonly int Dropped;
        /// <summary>Copies playing now.</summary>
        public readonly int Playing;
        /// <summary>-1 before the first.</summary>
        public readonly int LastVariant;

        public CueStats(SoundCue cue, int played, int dropped, int playing, int lastVariant)
        {
            Cue = cue;
            Played = played;
            Dropped = dropped;
            Playing = playing;
            LastVariant = lastVariant;
        }
    }
}
