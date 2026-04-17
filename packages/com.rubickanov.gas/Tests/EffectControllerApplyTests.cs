using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class EffectControllerApplyTests
    {
        private AttributeSet _attributes = null!;
        private GameplayTagContainer _tags = null!;
        private EffectController _controller = null!;
        private List<ActiveEffect> _applied = null!;
        private List<ActiveEffect> _removed = null!;
        private List<float> _healthChanged = null!;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            (_attributes, _tags, _controller) = GasTestFixtures.MakeTargetWithHealth(100f, 10f);
            _applied = new List<ActiveEffect>();
            _removed = new List<ActiveEffect>();
            _healthChanged = new List<float>();

            _controller.EffectApplied += OnApplied;
            _controller.EffectRemoved += OnRemoved;
            _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.ValueChanged += OnHealthChanged;
        }

        [TearDown]
        public void TearDown()
        {
            _controller.EffectApplied -= OnApplied;
            _controller.EffectRemoved -= OnRemoved;
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"));
            if (health != null) health.ValueChanged -= OnHealthChanged;
            GasTestFixtures.EnsureUninstalled();
        }

        private void OnApplied(ActiveEffect e) => _applied.Add(e);
        private void OnRemoved(ActiveEffect e) => _removed.Add(e);
        private void OnHealthChanged(float oldValue, float newValue) => _healthChanged.Add(newValue);

        [Test]
        public void ApplyEffect_InstantAddHealth_ModifiesBaseValue()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Instant,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f) });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(125f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyEffect_InstantEffect_ReturnsInvalidHandle()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Instant,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f) });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(ActiveEffectHandle.Invalid, handle);
        }

        [Test]
        public void ApplyEffect_InstantEffect_DoesNotFireEffectApplied()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Instant,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f) });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(0, _applied.Count);
        }

        [Test]
        public void ApplyEffect_InstantMultiply_ModifiesBaseValue()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Instant,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 2f) });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(200f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyEffect_InstantOverride_ReplacesBaseValue()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Instant,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 42f) });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(42f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyEffect_DurationEffect_ReturnsValidHandle()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) });

            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void ApplyEffect_DurationEffect_AppearsInActiveEffects()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f);

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(1, _controller.ActiveEffects.Count);
        }

        [Test]
        public void ApplyEffect_DurationAdd_CurrentValueUpdatedAndBaseUnchanged()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f) });

            _controller.ApplyEffect(new EffectSpec(def));
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;

            Assert.That(health.BaseValue, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
            Assert.That(health.CurrentValue, Is.EqualTo(125f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyEffect_DurationEffect_FiresEffectApplied()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f);

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(1, _applied.Count);
        }

        [Test]
        public void ApplyEffect_InfiniteEffect_DoesNotExpireOnLongTick()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Infinite);

            var handle = _controller.ApplyEffect(new EffectSpec(def));
            _controller.Tick(9999f);

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(1, _controller.ActiveEffects.Count);
        }

        [Test]
        public void ApplyEffect_MagnitudeScalesDurationModifier()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) });

            _controller.ApplyEffect(new EffectSpec(def, magnitude: 3f));
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;

            // 100 + (10 * 3) = 130
            Assert.That(health.CurrentValue, Is.EqualTo(130f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyEffect_WithGrantedTag_AddsTagToOwner()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                grantedTags: new[] { "Status.Stun" });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsTrue(_tags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));
        }

        [Test]
        public void ApplyEffect_ModifiesCurrentValue_RaisesAttributeValueChanged()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f) });

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(1, _healthChanged.Count);
            Assert.That(_healthChanged[0], Is.EqualTo(125f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
