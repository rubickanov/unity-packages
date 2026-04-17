namespace Rubickanov.GAS
{
    public enum DurationPolicy : byte
    {
        /// <summary>Applies modifiers to <see cref="GameplayAttribute.BaseValue"/> once and does not persist.</summary>
        Instant,
        /// <summary>Persists for <see cref="EffectDef.DurationSeconds"/>; modifies <see cref="GameplayAttribute.CurrentValue"/> via aggregation.</summary>
        Duration,
        /// <summary>Persists until removed; modifies <see cref="GameplayAttribute.CurrentValue"/> via aggregation.</summary>
        Infinite
    }
}
