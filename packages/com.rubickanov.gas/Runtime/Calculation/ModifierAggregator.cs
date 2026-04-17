using System.Collections.Generic;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    public static class ModifierAggregator
    {
        public static float Aggregate(float baseValue, GameplayTag attribute, IReadOnlyList<ActiveEffect> effects)
        {
            float addSum = 0f;
            float mulProduct = 1f;
            float overrideValue = 0f;
            int overridePriority = 0;
            bool hasOverride = false;

            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect.Def.Duration == DurationPolicy.Instant) continue;

                var modifiers = effect.Def.Modifiers;
                for (int j = 0; j < modifiers.Count; j++)
                {
                    var mod = modifiers[j];
                    if (mod.Attribute != attribute) continue;

                    float scaledValue = mod.Value * effect.Magnitude;

                    switch (mod.Operation)
                    {
                        case ModifierOp.Add:
                            addSum += scaledValue;
                            break;
                        case ModifierOp.Multiply:
                            mulProduct *= scaledValue;
                            break;
                        case ModifierOp.Override:
                            if (!hasOverride || mod.Priority >= overridePriority)
                            {
                                overrideValue = scaledValue;
                                overridePriority = mod.Priority;
                                hasOverride = true;
                            }
                            break;
                    }
                }
            }

            return hasOverride ? overrideValue : (baseValue + addSum) * mulProduct;
        }

        public static void ApplyInstant(AttributeSet attributes, Modifier modifier, float magnitude)
        {
            var attribute = attributes.Get(modifier.Attribute);
            if (attribute == null)
            {
                GasDiagnostics.EmitWarning(
                    $"GAS: instant modifier targets undefined attribute '{modifier.Attribute}'. Skipped.");
                return;
            }

            float scaledValue = modifier.Value * magnitude;

            switch (modifier.Operation)
            {
                case ModifierOp.Add:
                    attribute.BaseValue += scaledValue;
                    break;
                case ModifierOp.Multiply:
                    attribute.BaseValue *= scaledValue;
                    break;
                case ModifierOp.Override:
                    attribute.BaseValue = scaledValue;
                    break;
            }
        }
    }
}
