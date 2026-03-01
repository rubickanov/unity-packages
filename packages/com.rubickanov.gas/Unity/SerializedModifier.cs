using System;
using Rubickanov.GameplayTags;
using UnityEngine;

namespace Rubickanov.GAS
{
    [Serializable]
    public struct SerializedModifier
    {
        [SerializeField] private SerializedGameplayTag _attribute;
        [SerializeField] private ModifierOp _operation;
        [SerializeField] private float _value;

        public SerializedGameplayTag Attribute => _attribute;
        public ModifierOp Operation => _operation;
        public float Value => _value;

        public Modifier ToModifier()
        {
            return new Modifier(_attribute.Tag, _operation, _value);
        }
    }
}
