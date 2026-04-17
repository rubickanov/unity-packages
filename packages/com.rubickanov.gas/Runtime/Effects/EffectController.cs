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
        private readonly HashSet<GameplayTag> _dirtyAttributes = new();
        private int _nextHandleId = 1;

        public IReadOnlyList<ActiveEffect> ActiveEffects => _activeEffects;

        /// <summary>
        /// Fires AFTER the effect is inserted into <see cref="ActiveEffects"/>, its granted tags are
        /// added, and attributes are recalculated. Safe to call <see cref="ApplyEffect"/> or
        /// <see cref="RemoveEffect"/> from a handler; those operations take effect immediately.
        /// Not fired for Instant effects.
        /// </summary>
        public event Action<ActiveEffect>? EffectApplied;

        /// <summary>
        /// Fires AFTER the effect is removed from <see cref="ActiveEffects"/>, its granted tags are
        /// revoked (unless granted by another active effect), and attributes are recalculated.
        /// Safe to call <see cref="ApplyEffect"/> or <see cref="RemoveEffect"/> from a handler.
        /// </summary>
        public event Action<ActiveEffect>? EffectRemoved;

        public EffectController(AttributeSet attributes, GameplayTagContainer tags)
        {
            _attributes = attributes;
            _tags = tags;
            _attributes.BaseValueChanged += OnBaseValueChanged;
        }

        public ActiveEffectHandle ApplyEffect(EffectSpec spec)
        {
            var def = spec.Def;

            if (!CheckApplicationConditions(def)) return ActiveEffectHandle.Invalid;

            _dirtyAttributes.Clear();
            var removedDuringApply = _pendingRemoved;
            removedDuringApply.Clear();

            if (!def.RemoveEffectsWithTags.IsEmpty)
            {
                for (int i = _activeEffects.Count - 1; i >= 0; i--)
                {
                    var existing = _activeEffects[i];
                    if (MatchesAnyRemoveTag(existing.Def.EffectTag, def.RemoveEffectsWithTags))
                    {
                        CollectModifierAttributes(existing.Def.Modifiers, _dirtyAttributes);
                        RevokeGrantedTags(existing);
                        _activeEffects.RemoveAt(i);
                        removedDuringApply.Add(existing);
                    }
                }
            }

            if (def.Duration == DurationPolicy.Instant)
            {
                ApplyInstantModifiers(spec);
                CollectModifierAttributes(def.Modifiers, _dirtyAttributes);
                RecalculateAttributes(_dirtyAttributes);
                FireRemovedBatch(removedDuringApply);
                return ActiveEffectHandle.Invalid;
            }

            if (def.Stacking == StackingPolicy.Replace && def.EffectTag.IsValid)
            {
                for (int i = _activeEffects.Count - 1; i >= 0; i--)
                {
                    var existing = _activeEffects[i];
                    if (existing.Def.EffectTag == def.EffectTag)
                    {
                        CollectModifierAttributes(existing.Def.Modifiers, _dirtyAttributes);
                        RevokeGrantedTags(existing);
                        _activeEffects.RemoveAt(i);
                        removedDuringApply.Add(existing);
                    }
                }
            }

            var handle = new ActiveEffectHandle(_nextHandleId++);
            var activeEffect = new ActiveEffect(handle, spec);

            _activeEffects.Add(activeEffect);

            foreach (var tag in def.GrantedTags)
                _tags.AddTag(tag);

            CollectModifierAttributes(def.Modifiers, _dirtyAttributes);
            RecalculateAttributes(_dirtyAttributes);

            FireRemovedBatch(removedDuringApply);
            EffectApplied?.Invoke(activeEffect);

            return handle;
        }

        public int RemoveEffect(ActiveEffectHandle handle)
        {
            if (!handle.IsValid) return 0;

            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                if (_activeEffects[i].Handle == handle)
                {
                    var effect = _activeEffects[i];
                    _dirtyAttributes.Clear();
                    CollectModifierAttributes(effect.Def.Modifiers, _dirtyAttributes);
                    RevokeGrantedTags(effect);
                    _activeEffects.RemoveAt(i);
                    RecalculateAttributes(_dirtyAttributes);
                    EffectRemoved?.Invoke(effect);
                    return 1;
                }
            }

            return 0;
        }

        public int RemoveEffectsWithTag(GameplayTag tag)
        {
            if (!tag.IsValid) return 0;

            var removedEffects = _pendingRemoved;
            removedEffects.Clear();
            _dirtyAttributes.Clear();

            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                if (effect.Def.EffectTag.IsValid && effect.Def.EffectTag.Matches(tag))
                {
                    CollectModifierAttributes(effect.Def.Modifiers, _dirtyAttributes);
                    RevokeGrantedTags(effect);
                    _activeEffects.RemoveAt(i);
                    removedEffects.Add(effect);
                }
            }

            int removedCount = removedEffects.Count;
            if (removedCount == 0) return 0;

            RecalculateAttributes(_dirtyAttributes);
            FireRemovedBatch(removedEffects);
            return removedCount;
        }

        public int RemoveAllEffects()
        {
            int count = _activeEffects.Count;
            if (count == 0) return 0;

            var removedEffects = _pendingRemoved;
            removedEffects.Clear();
            _dirtyAttributes.Clear();

            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                CollectModifierAttributes(effect.Def.Modifiers, _dirtyAttributes);
                RevokeGrantedTags(effect);
                removedEffects.Add(effect);
            }

            _activeEffects.Clear();
            RecalculateAttributes(_dirtyAttributes);
            FireRemovedBatch(removedEffects);
            return count;
        }

        /// <summary>
        /// Advances durations, applies periodic modifiers, and removes expired effects.
        /// Effects applied from an <see cref="EffectRemoved"/> handler during this call begin
        /// ticking on the next frame, not the current one.
        /// </summary>
        public void Tick(float deltaTime)
        {
            bool dirty = false;
            var expired = _pendingRemoved;
            expired.Clear();

            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];

                bool willExpire = effect.Def.Duration == DurationPolicy.Duration;
                float remainingLifetime = willExpire
                    ? Math.Max(0f, effect.RemainingDuration)
                    : float.PositiveInfinity;

                if (effect.Def.Period > 0f)
                {
                    float periodicWindow = Math.Min(deltaTime, remainingLifetime);
                    if (periodicWindow > 0f)
                    {
                        effect.AdvancePeriod(periodicWindow);
                        while (effect.PeriodTimer >= effect.Def.Period)
                        {
                            effect.ConsumePeriod(effect.Def.Period);
                            ApplyPeriodicModifiers(effect);
                            dirty = true;
                        }
                    }
                }

                if (willExpire)
                {
                    effect.DecrementDuration(deltaTime);
                    if (effect.RemainingDuration <= 0f)
                    {
                        RevokeGrantedTags(effect);
                        _activeEffects.RemoveAt(i);
                        expired.Add(effect);
                        dirty = true;
                    }
                }
            }

            if (dirty) RecalculateAllAttributes();
            FireRemovedBatch(expired);
        }

        private bool CheckApplicationConditions(EffectDef def)
        {
            if (!def.ApplicationRequiredTags.IsEmpty && !_tags.HasAll(def.ApplicationRequiredTags))
                return false;

            if (!def.ApplicationBlockedTags.IsEmpty && _tags.HasAny(def.ApplicationBlockedTags))
                return false;

            return true;
        }

        private static bool MatchesAnyRemoveTag(GameplayTag effectTag, ReadOnlyGameplayTagContainer removeTags)
        {
            if (!effectTag.IsValid) return false;

            foreach (var removeTag in removeTags)
            {
                if (effectTag.Matches(removeTag))
                    return true;
            }

            return false;
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

        private void RevokeGrantedTags(ActiveEffect effect)
        {
            foreach (var tag in effect.Def.GrantedTags)
            {
                if (!IsTagGrantedByOtherEffect(tag, effect))
                    _tags.RemoveTag(tag);
            }
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

        private void OnBaseValueChanged(GameplayTag tag, float newBaseValue)
        {
            RecalculateAttribute(tag);
        }

        private static void CollectModifierAttributes(IReadOnlyList<Modifier> modifiers, HashSet<GameplayTag> buffer)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                var tag = modifiers[i].Attribute;
                if (tag.IsValid) buffer.Add(tag);
            }
        }

        private void RecalculateAttributes(HashSet<GameplayTag> tags)
        {
            foreach (var tag in tags)
                RecalculateAttribute(tag);
        }

        private void RecalculateAttribute(GameplayTag tag)
        {
            var attribute = _attributes.Get(tag);
            if (attribute == null) return;
            float newValue = ModifierAggregator.Aggregate(attribute.BaseValue, tag, _activeEffects);
            attribute.SetCurrentValue(newValue);
        }

        private void RecalculateAllAttributes()
        {
            foreach (var kvp in _attributes.All)
            {
                var attribute = kvp.Value;
                float newValue = ModifierAggregator.Aggregate(attribute.BaseValue, kvp.Key, _activeEffects);
                attribute.SetCurrentValue(newValue);
            }
        }

        private void FireRemovedBatch(List<ActiveEffect> removed)
        {
            if (removed.Count == 0) return;
            var snapshot = removed.ToArray();
            removed.Clear();
            for (int i = 0; i < snapshot.Length; i++)
                EffectRemoved?.Invoke(snapshot[i]);
        }

        private readonly List<ActiveEffect> _pendingRemoved = new();
    }
}
