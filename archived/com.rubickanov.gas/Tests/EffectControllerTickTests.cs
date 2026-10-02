using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class EffectControllerTickTests
    {
        private AttributeSet _attributes = null!;
        private GameplayTagContainer _tags = null!;
        private EffectController _controller = null!;
        private List<ActiveEffect> _removed = null!;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            (_attributes, _tags, _controller) = GasTestFixtures.MakeTargetWithHealth();
            _removed = new List<ActiveEffect>();
            _controller.EffectRemoved += OnRemoved;
        }

        [TearDown]
        public void TearDown()
        {
            _controller.EffectRemoved -= OnRemoved;
            GasTestFixtures.EnsureUninstalled();
        }

        private void OnRemoved(ActiveEffect e) => _removed.Add(e);

        [Test]
        public void Tick_DurationEffect_DecrementsRemainingDuration()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 5f);
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(2f);

            Assert.That(_controller.ActiveEffects[0].RemainingDuration,
                Is.EqualTo(3f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_DurationEffect_RemovesWhenExpired()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 1f);
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(1f);

            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }

        [Test]
        public void Tick_DurationEffect_ExpirationFiresEffectRemoved()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 1f);
            _controller.ApplyEffect(new EffectSpec(def));
            _removed.Clear();

            _controller.Tick(1.5f);

            Assert.AreEqual(1, _removed.Count);
        }

        [Test]
        public void Tick_DurationEffect_ExpirationRevokesGrantedTags()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 1f,
                grantedTags: new[] { "Status.Stun" });
            _controller.ApplyEffect(new EffectSpec(def));
            Assert.IsTrue(_tags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));

            _controller.Tick(1.5f);

            Assert.IsFalse(_tags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));
        }

        [Test]
        public void Tick_InfiniteEffect_DoesNotExpire()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Infinite);
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(9999f);

            Assert.AreEqual(1, _controller.ActiveEffects.Count);
        }

        [Test]
        public void Tick_PeriodicEffect_AppliesOnceAfterPeriod()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                period: 0.5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 5f) });
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(0.5f);

            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(105f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_PeriodicEffect_AppliesMultipleTimesWhenDeltaExceedsPeriod()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                period: 0.5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 5f) });
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(1.1f);

            // 1.1s / 0.5s = 2 applications
            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(110f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_PeriodicEffect_AccumulatesSmallDeltas()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                period: 0.5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 5f) });
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(0.2f);
            _controller.Tick(0.2f);
            _controller.Tick(0.2f); // total 0.6s → 1 application

            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(105f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_PeriodicEffect_ModifiesBaseValueViaApplyInstant()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                period: 1f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) });
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(1f);

            // Periodic uses ApplyInstant → mutates BaseValue (not just CurrentValue)
            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(110f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_PeriodicEffect_RecalculatesCurrentValueOfTargetAttribute()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                period: 1f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) });
            _controller.ApplyEffect(new EffectSpec(def));
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;
            float before = health.CurrentValue;

            _controller.Tick(1f);

            Assert.That(health.CurrentValue, Is.GreaterThan(before));
            Assert.That(health.CurrentValue, Is.EqualTo(
                ModifierAggregator.Aggregate(
                    health.BaseValue, GasTestFixtures.Tag("Attribute.Health"), _controller.ActiveEffects))
                .Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_PeriodicEffect_CurrentValueDoesNotDoubleCountModifier()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                period: 1f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, -3f) });
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;

            _controller.ApplyEffect(new EffectSpec(def));

            // No tick yet: the periodic modifier has not fired, so it must affect neither
            // BaseValue nor CurrentValue. Under the double-count bug CurrentValue was 97 here
            // (the aggregate folded the -3 a periodic effect realizes only via BaseValue).
            Assert.That(health.BaseValue, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
            Assert.That(health.CurrentValue, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));

            _controller.Tick(1f);

            // One period applied -3 to BaseValue; CurrentValue must equal BaseValue exactly,
            // not BaseValue - 3 again. Buggy code produced 94.
            Assert.That(health.BaseValue, Is.EqualTo(97f).Within(GasTestFixtures.FloatTolerance));
            Assert.That(health.CurrentValue, Is.EqualTo(97f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_PeriodicOnOneAttribute_LeavesUnrelatedAttributeIntact()
        {
            var speedBuff = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Speed", ModifierOp.Add, 5f) });
            _controller.ApplyEffect(new EffectSpec(speedBuff));
            var speed = _attributes.Get(GasTestFixtures.Tag("Attribute.Speed"))!;
            float speedAfterBuff = speed.CurrentValue;

            var healthDot = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                period: 1f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 5f) });
            _controller.ApplyEffect(new EffectSpec(healthDot));

            _controller.Tick(1f);

            Assert.That(speed.CurrentValue, Is.EqualTo(speedAfterBuff).Within(GasTestFixtures.FloatTolerance));
            Assert.That(speed.CurrentValue, Is.EqualTo(15f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_ZeroDelta_IsNoOp()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 5f);
            _controller.ApplyEffect(new EffectSpec(def));

            _controller.Tick(0f);

            Assert.AreEqual(1, _controller.ActiveEffects.Count);
            Assert.That(_controller.ActiveEffects[0].RemainingDuration,
                Is.EqualTo(5f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_EmptyController_IsNoOp()
        {
            Assert.DoesNotThrow(() => _controller.Tick(1f));
            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }
    }
}
