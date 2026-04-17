namespace Rubickanov.GAS
{
    /// <summary>
    /// A runtime request to apply an <see cref="EffectDef"/> with an optional source and a magnitude scalar.
    /// </summary>
    public sealed class EffectSpec
    {
        public EffectDef Def { get; }

        /// <summary>Optional originator (caster, item, etc.) — carried through to <see cref="ActiveEffect.Source"/>.</summary>
        public object? Source { get; }

        /// <summary>
        /// Scalar applied to each modifier's Value at apply/aggregate time. Scales the input to the aggregator,
        /// not the result: <c>Add 10</c> with magnitude 2 contributes +20, <c>Multiply 2</c> with magnitude 0.5
        /// contributes *1.0 (not *1.41).
        /// </summary>
        public float Magnitude { get; }

        public EffectSpec(EffectDef def, object? source = null, float magnitude = 1f)
        {
            Def = def;
            Source = source;
            Magnitude = magnitude;
        }
    }
}
