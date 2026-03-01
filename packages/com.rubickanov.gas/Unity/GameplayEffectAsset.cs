using System.Collections.Generic;
using Rubickanov.GameplayTags;
using UnityEngine;

namespace Rubickanov.GAS
{
    [CreateAssetMenu(menuName = "GAS/Gameplay Effect")]
    public class GameplayEffectAsset : ScriptableObject
    {
        [SerializeField] private DurationPolicy _duration;
        [SerializeField] private float _durationSeconds;
        [SerializeField] private float _period;
        [SerializeField] private StackingPolicy _stacking;
        [SerializeField] private List<SerializedModifier> _modifiers = new();
        [SerializeField] private SerializedGameplayTag _effectTag;
        [SerializeField] private SerializedGameplayTagContainer _grantedTags;
        [SerializeField] private SerializedGameplayTagContainer _requiredTags;
        [SerializeField] private SerializedGameplayTagContainer _blockedTags;
        [SerializeField] private SerializedGameplayTagContainer _removeEffectsWithTags;

        public EffectDef ToDef()
        {
            var modifiers = new Modifier[_modifiers.Count];
            for (int i = 0; i < _modifiers.Count; i++)
                modifiers[i] = _modifiers[i].ToModifier();

            return new EffectDef(
                _duration,
                _durationSeconds,
                _period,
                modifiers,
                _grantedTags.Container,
                _requiredTags.Container,
                _blockedTags.Container,
                _removeEffectsWithTags.Container,
                _effectTag.Tag,
                _stacking);
        }

        public EffectSpec CreateSpec(object? source = null, float magnitude = 1f)
        {
            return new EffectSpec(ToDef(), source, magnitude);
        }
    }
}
