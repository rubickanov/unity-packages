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
        public GameplayTagContainer GrantedTags { get; }
        public GameplayTagContainer ApplicationRequiredTags { get; }
        public GameplayTagContainer ApplicationBlockedTags { get; }
        public GameplayTagContainer RemoveEffectsWithTags { get; }
        public GameplayTag EffectTag { get; }
        public StackingPolicy Stacking { get; }

        public EffectDef(
            DurationPolicy duration,
            float durationSeconds,
            float period,
            IReadOnlyList<Modifier> modifiers,
            GameplayTagContainer grantedTags,
            GameplayTagContainer applicationRequiredTags,
            GameplayTagContainer applicationBlockedTags,
            GameplayTagContainer removeEffectsWithTags,
            GameplayTag effectTag,
            StackingPolicy stacking)
        {
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
