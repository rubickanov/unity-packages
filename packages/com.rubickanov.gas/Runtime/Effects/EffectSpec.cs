namespace Rubickanov.GAS
{
    public sealed class EffectSpec
    {
        public EffectDef Def { get; }
        public object? Source { get; }
        public float Magnitude { get; }

        public EffectSpec(EffectDef def, object? source = null, float magnitude = 1f)
        {
            Def = def;
            Source = source;
            Magnitude = magnitude;
        }
    }
}
