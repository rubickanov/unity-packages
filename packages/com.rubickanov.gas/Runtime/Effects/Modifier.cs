using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    /// <summary>
    /// Single arithmetic operation against a single attribute. Composed into <see cref="EffectDef.Modifiers"/>.
    /// </summary>
    public readonly struct Modifier
    {
        public readonly GameplayTag Attribute;
        public readonly ModifierOp Operation;
        public readonly float Value;

        /// <summary>
        /// Tie-breaker for <see cref="ModifierOp.Override"/>. Highest wins; equal priorities resolve
        /// to last applied. Ignored by Add/Multiply.
        /// </summary>
        public readonly int Priority;

        public Modifier(GameplayTag attribute, ModifierOp operation, float value, int priority = 0)
        {
            Attribute = attribute;
            Operation = operation;
            Value = value;
            Priority = priority;
        }
    }
}
