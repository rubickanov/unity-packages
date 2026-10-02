using System.Collections.Generic;
using Rubickanov.GameplayTags;
using UnityEngine;

namespace Rubickanov.GAS
{
    [CreateAssetMenu(menuName = "GAS/Gameplay Effect")]
    public class GameplayEffectAsset : ScriptableObject
    {
        [Tooltip("Instant = modify BaseValue once; Duration = persist for DurationSeconds; Infinite = persist until removed.")]
        [SerializeField] private DurationPolicy _duration;

        [Tooltip("Duration in seconds. Ignored when Duration is Instant or Infinite.")]
        [SerializeField, Min(0f)] private float _durationSeconds;

        [Tooltip("Periodic tick interval in seconds. 0 = no periodic tick. Periodic modifiers mutate BaseValue each tick (same as an Instant application).")]
        [SerializeField, Min(0f)] private float _period;

        [Tooltip("Independent = multiple instances coexist; Replace = re-applying with the same EffectTag removes the previous instance.")]
        [SerializeField] private StackingPolicy _stacking;

        [Tooltip("Modifiers applied to attributes while the effect is active (or once, for Instant).")]
        [SerializeField] private List<SerializedModifier> _modifiers = new();

        [Tooltip("Optional tag identifying this effect (used by Replace stacking, RemoveEffectsWithTag, RemoveEffectsWithTags).")]
        [SerializeField] private SerializedGameplayTag _effectTag;

        [Tooltip("Tags granted to the owner while the effect is active.")]
        [SerializeField] private SerializedGameplayTagContainer _grantedTags;

        [Tooltip("Owner must have ALL these tags for the effect to apply.")]
        [SerializeField] private SerializedGameplayTagContainer _requiredTags;

        [Tooltip("Owner must have NONE of these tags for the effect to apply.")]
        [SerializeField] private SerializedGameplayTagContainer _blockedTags;

        [Tooltip("On application, remove active effects whose EffectTag matches (is equal to or a descendant of) any of these tags.")]
        [SerializeField] private SerializedGameplayTagContainer _removeEffectsWithTags;

        public EffectDef ToDef()
        {
            var modifiers = new Modifier[_modifiers.Count];
            for (int i = 0; i < _modifiers.Count; i++)
            {
                var serialized = _modifiers[i];
                if (!serialized.Attribute.Tag.IsValid)
                    Debug.LogWarning(
                        $"GAS: '{name}' modifier[{i}] has no attribute assigned — it will be ignored at runtime.",
                        this);
                modifiers[i] = serialized.ToModifier();
            }

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

        private void OnValidate()
        {
            if (_durationSeconds < 0f) _durationSeconds = 0f;
            if (_period < 0f) _period = 0f;

            if (_duration == DurationPolicy.Instant)
            {
                _durationSeconds = 0f;
                _period = 0f;
            }

            if (_duration == DurationPolicy.Duration && _period > 0f && _period > _durationSeconds)
                Debug.LogWarning(
                    $"GAS: '{name}' has period ({_period}) greater than duration ({_durationSeconds}) — it will never tick.",
                    this);
        }
    }
}
