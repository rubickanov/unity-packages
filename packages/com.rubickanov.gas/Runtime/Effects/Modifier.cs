using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    public readonly struct Modifier
    {
        public readonly GameplayTag Attribute;
        public readonly ModifierOp Operation;
        public readonly float Value;

        public Modifier(GameplayTag attribute, ModifierOp operation, float value)
        {
            Attribute = attribute;
            Operation = operation;
            Value = value;
        }
    }
}
