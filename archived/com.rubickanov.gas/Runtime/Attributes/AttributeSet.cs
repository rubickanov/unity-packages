using System;
using System.Collections.Generic;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    /// <summary>
    /// A collection of <see cref="GameplayAttribute"/> keyed by <see cref="GameplayTag"/>. Publishes
    /// <see cref="BaseValueChanged"/> on every successful <see cref="SetBaseValue"/> so that an
    /// <see cref="EffectController"/> (or any other observer) can recalculate derived values.
    /// </summary>
    public sealed class AttributeSet
    {
        private readonly Dictionary<GameplayTag, GameplayAttribute> _attributes = new();

        /// <summary>Fires whenever <see cref="SetBaseValue"/> is called. Arguments are (attributeTag, newBaseValue).</summary>
        public event Action<GameplayTag, float>? BaseValueChanged;

        /// <summary>Registers a new attribute. Throws if one already exists for <paramref name="tag"/>.</summary>
        public GameplayAttribute Define(GameplayTag tag, float baseValue = 0f)
        {
            if (_attributes.ContainsKey(tag))
                throw new InvalidOperationException(
                    $"Attribute '{tag}' is already defined. Use SetBaseValue to change its base value.");

            var attribute = new GameplayAttribute(baseValue);
            _attributes[tag] = attribute;
            return attribute;
        }

        public GameplayAttribute? Get(GameplayTag tag)
        {
            return _attributes.TryGetValue(tag, out var attribute) ? attribute : null;
        }

        public bool TryGet(GameplayTag tag, out GameplayAttribute? attribute)
        {
            if (_attributes.TryGetValue(tag, out var found))
            {
                attribute = found;
                return true;
            }

            attribute = null;
            return false;
        }

        /// <summary>
        /// Sets the base value of an existing attribute and fires <see cref="BaseValueChanged"/>.
        /// Throws if the attribute has not been defined.
        /// </summary>
        public void SetBaseValue(GameplayTag tag, float value)
        {
            if (!_attributes.TryGetValue(tag, out var attribute))
                throw new InvalidOperationException(
                    $"Attribute '{tag}' is not defined. Call Define first.");

            attribute.BaseValue = value;
            BaseValueChanged?.Invoke(tag, value);
        }
    }
}
