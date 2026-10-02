namespace Rubickanov.GAS
{
    public enum StackingPolicy : byte
    {
        /// <summary>Multiple instances of the same effect coexist independently.</summary>
        Independent,
        /// <summary>Applying a new instance with the same <see cref="EffectDef.EffectTag"/> removes any existing instance.</summary>
        Replace
    }
}
