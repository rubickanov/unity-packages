using System;
using System.Collections.Generic;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    public sealed class EffectController
    {
        private readonly AttributeSet _attributes;
        private readonly GameplayTagContainer _tags;
        private readonly List<ActiveEffect> _activeEffects = new();
        private readonly List<ActiveEffect> _activeEffectsReadOnly;
        private int _nextHandleId = 1;

        public IReadOnlyList<ActiveEffect> ActiveEffects => _activeEffectsReadOnly;

        public event Action<ActiveEffect>? EffectApplied;
        public event Action<ActiveEffect>? EffectRemoved;

        public EffectController(AttributeSet attributes, GameplayTagContainer tags)
        {
            _attributes = attributes;
            _tags = tags;
            _activeEffectsReadOnly = _activeEffects;
        }

        public ActiveEffectHandle ApplyEffect(EffectSpec spec)
        {
            var def = spec.Def;

            // Check application conditions
            if (!CheckApplicationConditions(def)) return ActiveEffectHandle.Invalid;

            // Remove effects with specified tags
            if (!def.RemoveEffectsWithTags.IsEmpty)
            {
                for (int i = _activeEffects.Count - 1; i >= 0; i--)
                {
                    var existing = _activeEffects[i];
                    if (existing.Def.EffectTag.IsValid &&
                        def.RemoveEffectsWithTags.HasTag(existing.Def.EffectTag))
                    {
                        RemoveEffectInternal(existing);
                        _activeEffects.RemoveAt(i);
                    }
                }
            }

            // Handle instant effects
            if (def.Duration == DurationPolicy.Instant)
            {
                ApplyInstantModifiers(spec);
                RecalculateAttributes();
                return ActiveEffectHandle.Invalid;
            }

            // Handle stacking
            if (def.Stacking == StackingPolicy.Replace && def.EffectTag.IsValid)
            {
                for (int i = _activeEffects.Count - 1; i >= 0; i--)
                {
                    var existing = _activeEffects[i];
                    if (existing.Def.EffectTag == def.EffectTag)
                    {
                        RemoveEffectInternal(existing);
                        _activeEffects.RemoveAt(i);
                    }
                }
            }

            // Create active effect
            var handle = new ActiveEffectHandle(_nextHandleId++);
            var activeEffect = new ActiveEffect(handle, spec);

            _activeEffects.Add(activeEffect);

            // Grant tags
            foreach (var tag in def.GrantedTags)
                _tags.AddTag(tag);

            RecalculateAttributes();

            EffectApplied?.Invoke(activeEffect);

            return handle;
        }

        public bool RemoveEffect(ActiveEffectHandle handle)
        {
            if (!handle.IsValid) return false;

            for (int i = 0; i < _activeEffects.Count; i++)
            {
                if (_activeEffects[i].Handle == handle)
                {
                    var effect = _activeEffects[i];
                    RemoveEffectInternal(effect);
                    _activeEffects.RemoveAt(i);
                    RecalculateAttributes();
                    return true;
                }
            }

            return false;
        }

        public int RemoveEffectsWithTag(GameplayTag tag)
        {
            if (!tag.IsValid) return 0;

            int removed = 0;
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                if (effect.Def.EffectTag.IsValid && effect.Def.EffectTag.Matches(tag))
                {
                    RemoveEffectInternal(effect);
                    _activeEffects.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0) RecalculateAttributes();
            return removed;
        }

        public void RemoveAllEffects()
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
                RemoveEffectInternal(_activeEffects[i]);

            _activeEffects.Clear();
            RecalculateAttributes();
        }

        public void Tick(float deltaTime)
        {
            bool dirty = false;

            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];

                // Handle periodic effects
                if (effect.Def.Period > 0f)
                {
                    effect.PeriodTimer += deltaTime;
                    while (effect.PeriodTimer >= effect.Def.Period)
                    {
                        effect.PeriodTimer -= effect.Def.Period;
                        ApplyPeriodicModifiers(effect);
                        dirty = true;
                    }
                }

                // Handle duration
                if (effect.Def.Duration == DurationPolicy.Duration)
                {
                    effect.RemainingDuration -= deltaTime;
                    if (effect.RemainingDuration <= 0f)
                    {
                        RemoveEffectInternal(effect);
                        _activeEffects.RemoveAt(i);
                        dirty = true;
                    }
                }
            }

            if (dirty) RecalculateAttributes();
        }

        private bool CheckApplicationConditions(EffectDef def)
        {
            // Required tags: target must have all of them
            if (!def.ApplicationRequiredTags.IsEmpty && !_tags.HasAll(def.ApplicationRequiredTags))
                return false;

            // Blocked tags: target must have none of them
            if (!def.ApplicationBlockedTags.IsEmpty && _tags.HasAny(def.ApplicationBlockedTags))
                return false;

            return true;
        }

        private void ApplyInstantModifiers(EffectSpec spec)
        {
            var modifiers = spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
                ModifierAggregator.ApplyInstant(_attributes, modifiers[i], spec.Magnitude);
        }

        private void ApplyPeriodicModifiers(ActiveEffect effect)
        {
            var modifiers = effect.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
                ModifierAggregator.ApplyInstant(_attributes, modifiers[i], effect.Magnitude);
        }

        private void RemoveEffectInternal(ActiveEffect effect)
        {
            // Remove granted tags (only if no other active effect grants the same tag)
            foreach (var tag in effect.Def.GrantedTags)
            {
                if (!IsTagGrantedByOtherEffect(tag, effect))
                    _tags.RemoveTag(tag);
            }

            EffectRemoved?.Invoke(effect);
        }

        private bool IsTagGrantedByOtherEffect(GameplayTag tag, ActiveEffect excludeEffect)
        {
            for (int i = 0; i < _activeEffects.Count; i++)
            {
                var other = _activeEffects[i];
                if (other.Handle == excludeEffect.Handle) continue;
                if (other.Def.GrantedTags.HasTagExact(tag)) return true;
            }
            return false;
        }

        private void RecalculateAttributes()
        {
            foreach (var kvp in _attributes.All)
            {
                var attribute = kvp.Value;
                float newValue = ModifierAggregator.Aggregate(attribute.BaseValue, kvp.Key, _activeEffects);
                attribute.SetCurrentValue(newValue);
            }
        }
    }
}
