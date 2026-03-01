using System.Collections.Generic;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    public sealed class AttributeSet
    {
        private readonly Dictionary<GameplayTag, GameplayAttribute> _attributes = new();

        public GameplayAttribute Define(GameplayTag tag, float baseValue = 0f)
        {
            if (_attributes.TryGetValue(tag, out var existing))
                return existing;

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

        internal IEnumerable<KeyValuePair<GameplayTag, GameplayAttribute>> All => _attributes;
    }
}
