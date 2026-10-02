namespace Rubickanov.GAS
{
    /// <summary>
    /// Runtime state of an effect tracked by an <see cref="EffectController"/>. Created for
    /// <see cref="DurationPolicy.Duration"/> and <see cref="DurationPolicy.Infinite"/> effects.
    /// </summary>
    public sealed class ActiveEffect
    {
        public ActiveEffectHandle Handle { get; }
        public EffectDef Def { get; }
        public object? Source { get; }
        public float Magnitude { get; }

        /// <summary>Seconds remaining. Meaningful only for <see cref="DurationPolicy.Duration"/>.</summary>
        public float RemainingDuration { get; private set; }

        /// <summary>Accumulator for periodic ticks. Advances by delta each <see cref="EffectController.Tick"/>.</summary>
        public float PeriodTimer { get; private set; }

        internal ActiveEffect(ActiveEffectHandle handle, EffectSpec spec)
        {
            Handle = handle;
            Def = spec.Def;
            Source = spec.Source;
            Magnitude = spec.Magnitude;
            RemainingDuration = spec.Def.Duration == DurationPolicy.Duration ? spec.Def.DurationSeconds : 0f;
            PeriodTimer = 0f;
        }

        internal void DecrementDuration(float deltaTime) => RemainingDuration -= deltaTime;
        internal void AdvancePeriod(float deltaTime) => PeriodTimer += deltaTime;
        internal void ConsumePeriod(float period) => PeriodTimer -= period;
    }
}
