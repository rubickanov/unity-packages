namespace Rubickanov.GAS
{
    public enum ModifierOp : byte
    {
        /// <summary>Added to the base value. Multiple Adds sum together.</summary>
        Add,
        /// <summary>Multiplied against the (base + addSum) intermediate. Multiple Multiplies compose multiplicatively.</summary>
        Multiply,
        /// <summary>Replaces the value entirely. Wins over Add/Multiply. With multiple Overrides, highest <see cref="Modifier.Priority"/> wins; ties resolve to last applied.</summary>
        Override
    }
}
