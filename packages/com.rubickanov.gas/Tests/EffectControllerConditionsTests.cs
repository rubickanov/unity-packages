using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class EffectControllerConditionsTests
    {
        private AttributeSet _attributes = null!;
        private GameplayTagContainer _tags = null!;
        private EffectController _controller = null!;
        private List<ActiveEffect> _applied = null!;
        private List<ActiveEffect> _removed = null!;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            (_attributes, _tags, _controller) = GasTestFixtures.MakeTargetWithHealth();
            _applied = new List<ActiveEffect>();
            _removed = new List<ActiveEffect>();
            _controller.EffectApplied += OnApplied;
            _controller.EffectRemoved += OnRemoved;
        }

        [TearDown]
        public void TearDown()
        {
            _controller.EffectApplied -= OnApplied;
            _controller.EffectRemoved -= OnRemoved;
            GasTestFixtures.EnsureUninstalled();
        }

        private void OnApplied(ActiveEffect e) => _applied.Add(e);
        private void OnRemoved(ActiveEffect e) => _removed.Add(e);

        // ---- Required tags ----

        [Test]
        public void ApplyEffect_RequiredTagsPresent_Applies()
        {
            _tags.AddTag(GasTestFixtures.Tag("Status.Stun"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                requiredTags: new[] { "Status.Stun" });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_RequiredTagsMissing_ReturnsInvalidHandle()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                requiredTags: new[] { "Status.Stun" });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_RequiredTagsPartial_ReturnsInvalidHandle()
        {
            _tags.AddTag(GasTestFixtures.Tag("Status.Stun"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                requiredTags: new[] { "Status.Stun", "Debuff.Burn" });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_RequiredTagsSatisfiedHierarchically_Applies()
        {
            // Owner has Damage.Fire → HasAll({Damage}) returns true via hierarchy
            _tags.AddTag(GasTestFixtures.Tag("Damage.Fire"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                requiredTags: new[] { "Damage" });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsTrue(handle.IsValid);
        }

        // ---- Blocked tags ----

        [Test]
        public void ApplyEffect_BlockedTagPresent_ReturnsInvalidHandle()
        {
            _tags.AddTag(GasTestFixtures.Tag("Immune"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                blockedTags: new[] { "Immune" });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_BlockedTagPresentHierarchically_ReturnsInvalidHandle()
        {
            // Owner has Immune.Stun → HasAny({Immune}) returns true via hierarchy
            _tags.AddTag(GasTestFixtures.Tag("Immune.Stun"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                blockedTags: new[] { "Immune" });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_BlockedTagAbsent_Applies()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                blockedTags: new[] { "Immune" });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_NoConditions_AlwaysApplies()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f);

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_BlockedEffect_DoesNotFireEffectApplied()
        {
            _tags.AddTag(GasTestFixtures.Tag("Immune"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                blockedTags: new[] { "Immune" });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(0, _applied.Count);
        }

        [Test]
        public void ApplyEffect_BlockedEffect_DoesNotModifyAttributes()
        {
            _tags.AddTag(GasTestFixtures.Tag("Immune"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f) },
                blockedTags: new[] { "Immune" });

            _controller.ApplyEffect(new EffectSpec(def));
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;

            Assert.That(health.BaseValue, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
            Assert.That(health.CurrentValue, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyEffect_BlockedEffect_DoesNotGrantTags()
        {
            _tags.AddTag(GasTestFixtures.Tag("Immune"));
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                grantedTags: new[] { "Status.Stun" },
                blockedTags: new[] { "Immune" });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsFalse(_tags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));
        }

        // ---- RemoveEffectsWithTags ----

        [Test]
        public void ApplyEffect_RemoveEffectsWithTags_RemovesExistingWithMatchingEffectTag()
        {
            var prior = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Debuff.Burn");
            _controller.ApplyEffect(new EffectSpec(prior));
            Assert.AreEqual(1, _controller.ActiveEffects.Count);

            var replacer = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                removeEffectsWithTags: new[] { "Debuff.Burn" },
                effectTag: "Effect.Heal");
            _controller.ApplyEffect(new EffectSpec(replacer));

            Assert.AreEqual(1, _controller.ActiveEffects.Count);
            Assert.AreEqual(GasTestFixtures.Tag("Effect.Heal"),
                _controller.ActiveEffects[0].Def.EffectTag);
        }

        [Test]
        public void ApplyEffect_RemoveEffectsWithTags_RemovesExistingWhenEffectTagIsDescendantOfQuery()
        {
            // Hierarchy-aware: existing.EffectTag.Matches(queryTag) — existing is removed when its
            // EffectTag is equal to or a descendant of any tag in RemoveEffectsWithTags.
            // Cleanser with broad "Debuff" removes the more specific "Debuff.Burn" effect.
            var existing = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Debuff.Burn");
            _controller.ApplyEffect(new EffectSpec(existing));

            var cleanser = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                removeEffectsWithTags: new[] { "Debuff" },
                effectTag: "Effect.Heal");
            _controller.ApplyEffect(new EffectSpec(cleanser));

            Assert.AreEqual(1, _controller.ActiveEffects.Count);
            Assert.AreEqual(GasTestFixtures.Tag("Effect.Heal"),
                _controller.ActiveEffects[0].Def.EffectTag);
        }

        [Test]
        public void ApplyEffect_RemoveEffectsWithTags_DoesNotRemoveWhenExistingIsAncestorOfQuery()
        {
            // Hierarchy-aware: a cleanser with specific "Debuff.Burn" does NOT remove broader "Debuff"
            // (Debuff.Matches(Debuff.Burn) is false — Debuff is an ancestor, not a descendant).
            var existing = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Debuff");
            _controller.ApplyEffect(new EffectSpec(existing));

            var cleanser = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                removeEffectsWithTags: new[] { "Debuff.Burn" },
                effectTag: "Effect.Heal");
            _controller.ApplyEffect(new EffectSpec(cleanser));

            Assert.AreEqual(2, _controller.ActiveEffects.Count);
        }

        [Test]
        public void ApplyEffect_RemoveEffectsWithTags_FiresEffectRemoved()
        {
            var prior = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Debuff.Burn");
            _controller.ApplyEffect(new EffectSpec(prior));
            _removed.Clear();

            var replacer = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                removeEffectsWithTags: new[] { "Debuff.Burn" },
                effectTag: "Effect.Heal");
            _controller.ApplyEffect(new EffectSpec(replacer));

            Assert.AreEqual(1, _removed.Count);
            Assert.AreEqual(GasTestFixtures.Tag("Debuff.Burn"), _removed[0].Def.EffectTag);
        }
    }
}
