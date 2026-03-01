namespace Rubickanov.GAS
{
    public sealed class ActiveEffect
    {
        public ActiveEffectHandle Handle { get; }
        public EffectDef Def { get; }
        public object? Source { get; }
        public float Magnitude { get; }
        public float RemainingDuration { get; internal set; }
        public float PeriodTimer { get; internal set; }

        internal ActiveEffect(ActiveEffectHandle handle, EffectSpec spec)
        {
            Handle = handle;
            Def = spec.Def;
            Source = spec.Source;
            Magnitude = spec.Magnitude;
            RemainingDuration = spec.Def.Duration == DurationPolicy.Infinite ? -1f : spec.Def.DurationSeconds;
            PeriodTimer = 0f;
        }
    }
}
