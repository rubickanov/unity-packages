using System;
using System.Collections.Generic;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS
{
    public sealed class EffectDef
    {
        public DurationPolicy Duration { get; }
        public float DurationSeconds { get; }
        public float Period { get; }
        public IReadOnlyList<Modifier> Modifiers { get; }
        public ReadOnlyGameplayTagContainer GrantedTags { get; }
        public ReadOnlyGameplayTagContainer ApplicationRequiredTags { get; }
        public ReadOnlyGameplayTagContainer ApplicationBlockedTags { get; }
        public ReadOnlyGameplayTagContainer RemoveEffectsWithTags { get; }
        public GameplayTag EffectTag { get; }
        public StackingPolicy Stacking { get; }

        public EffectDef(
            DurationPolicy duration,
            float durationSeconds,
            float period,
            IReadOnlyList<Modifier> modifiers,
            ReadOnlyGameplayTagContainer grantedTags,
            ReadOnlyGameplayTagContainer applicationRequiredTags,
            ReadOnlyGameplayTagContainer applicationBlockedTags,
            ReadOnlyGameplayTagContainer removeEffectsWithTags,
            GameplayTag effectTag,
            StackingPolicy stacking)
        {
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));
            if (durationSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Must be >= 0.");
            if (period < 0f)
                throw new ArgumentOutOfRangeException(nameof(period), "Must be >= 0.");

            if (duration == DurationPolicy.Instant)
            {
                durationSeconds = 0f;
                period = 0f;
            }

            Duration = duration;
            DurationSeconds = durationSeconds;
            Period = period;
            Modifiers = modifiers;
            GrantedTags = grantedTags;
            ApplicationRequiredTags = applicationRequiredTags;
            ApplicationBlockedTags = applicationBlockedTags;
            RemoveEffectsWithTags = removeEffectsWithTags;
            EffectTag = effectTag;
            Stacking = stacking;
        }
    }
}
