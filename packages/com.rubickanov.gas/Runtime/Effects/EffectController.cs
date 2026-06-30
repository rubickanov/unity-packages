using System;
using System.Collections.Generic;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    public sealed class EffectController : IDisposable
    {
        private readonly AttributeSet _attributes;
        private readonly GameplayTagContainer _tags;
        private readonly List<ActiveEffect> _activeEffects = new();
        private readonly HashSet<GameplayTag> _dirtyAttributes = new();
        private int _nextHandleId = 1;
        private bool _disposed;

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

        /// <summary>
        /// Detaches from <see cref="AttributeSet.BaseValueChanged"/>. Call when the controller
        /// outlives its usefulness but the <see cref="AttributeSet"/> lives on (re-init, pooling,
        /// respawn reusing the same attribute set), otherwise the stale controller stays
        /// subscribed and is kept alive by the event. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _attributes.BaseValueChanged -= OnBaseValueChanged;
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
                var instantRemovedSnapshot = DetachRemoved(removedDuringApply);
                RecalculateAttributes(_dirtyAttributes);
                FireRemoved(instantRemovedSnapshot);
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
            var removedSnapshot = DetachRemoved(removedDuringApply);
            RecalculateAttributes(_dirtyAttributes);

            FireRemoved(removedSnapshot);
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

            var removedSnapshot = DetachRemoved(removedEffects);
            RecalculateAttributes(_dirtyAttributes);
            FireRemoved(removedSnapshot);
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
            var removedSnapshot = DetachRemoved(removedEffects);
            RecalculateAttributes(_dirtyAttributes);
            FireRemoved(removedSnapshot);
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
            _dirtyAttributes.Clear();

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
                        bool periodicFired = false;
                        while (effect.PeriodTimer >= effect.Def.Period)
                        {
                            effect.ConsumePeriod(effect.Def.Period);
                            ApplyPeriodicModifiers(effect);
                            periodicFired = true;
                        }

                        if (periodicFired)
                        {
                            // Only the attributes this periodic modifier writes need recalculating.
                            CollectModifierAttributes(effect.Def.Modifiers, _dirtyAttributes);
                            dirty = true;
                        }
                    }
                }

                if (willExpire)
                {
                    effect.DecrementDuration(deltaTime);
                    if (effect.RemainingDuration <= 0f)
                    {
                        // Removing this effect changes only the attributes its modifiers target.
                        CollectModifierAttributes(effect.Def.Modifiers, _dirtyAttributes);
                        RevokeGrantedTags(effect);
                        _activeEffects.RemoveAt(i);
                        expired.Add(effect);
                        dirty = true;
                    }
                }
            }

            var expiredSnapshot = DetachRemoved(expired);
            // Recalculate only the touched attributes (same targeted pattern as Apply/RemoveEffect),
            // not the whole set: a single DoT/regen otherwise pays O(attributes × effects × modifiers).
            if (dirty) RecalculateAttributes(_dirtyAttributes);
            FireRemoved(expiredSnapshot);
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
            int count = tags.Count;
            if (count == 0) return;

            // Snapshot the dirty tags before recalculating. RecalculateAttribute fires
            // GameplayAttribute.ValueChanged, and a handler may reentrantly Apply/RemoveEffect,
            // which clears the shared _dirtyAttributes. Iterating a local copy keeps this loop
            // valid through that reentrancy instead of throwing "Collection was modified".
            Span<GameplayTag> buffer = count <= 32 ? stackalloc GameplayTag[count] : new GameplayTag[count];
            int i = 0;
            foreach (var tag in tags)
                buffer[i++] = tag;

            for (int j = 0; j < count; j++)
                RecalculateAttribute(buffer[j]);
        }

        private void RecalculateAttribute(GameplayTag tag)
        {
            var attribute = _attributes.Get(tag);
            if (attribute == null) return;
            float newValue = ModifierAggregator.Aggregate(attribute.BaseValue, tag, _activeEffects);
            attribute.SetCurrentValue(newValue);
        }

        // Detaches the pending-removed effects into an owned array and clears the shared buffer.
        // Called BEFORE recalculation so a ValueChanged-triggered reentrant Apply/RemoveEffect
        // (which reuses _pendingRemoved) cannot wipe this call's removals before they are fired.
        private static ActiveEffect[] DetachRemoved(List<ActiveEffect> removed)
        {
            if (removed.Count == 0) return Array.Empty<ActiveEffect>();
            var snapshot = removed.ToArray();
            removed.Clear();
            return snapshot;
        }

        private void FireRemoved(ActiveEffect[] removed)
        {
            for (int i = 0; i < removed.Length; i++)
                EffectRemoved?.Invoke(removed[i]);
        }

        private readonly List<ActiveEffect> _pendingRemoved = new();
    }
}
