using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class ModifierAggregatorTests
    {
        private AttributeSet _attributes = null!;
        private GameplayTagContainer _ownerTags = null!;
        private EffectController _controller = null!;
        private GameplayTag _health;
        private GameplayTag _speed;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            (_attributes, _ownerTags, _controller) = GasTestFixtures.MakeTargetWithHealth(100f, 10f);
            _health = GasTestFixtures.Tag("Attribute.Health");
            _speed = GasTestFixtures.Tag("Attribute.Speed");
        }

        [TearDown]
        public void TearDown()
        {
            GasTestFixtures.EnsureUninstalled();
        }

        // ---- Aggregate ----

        [Test]
        public void Aggregate_NoEffects_ReturnsBaseValue()
        {
            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_SingleAdd_AddsToBase()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(125f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_MultipleAdds_Accumulate()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 20f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 5f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(135f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_SingleMultiply_MultipliesAfterBase()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 2f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(200f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_MultipleMultiplies_ComposeMultiplicatively()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 2f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 1.5f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            // (100 + 0) * 2 * 1.5 = 300
            Assert.That(result, Is.EqualTo(300f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_AddAndMultiply_MultipliesAfterSum()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 2f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            // (100 + 10) * 2 = 220
            Assert.That(result, Is.EqualTo(220f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_Override_IgnoresAddAndMultiply()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 500f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 10f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 42f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(42f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_MultipleOverrides_LastWins()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 10f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 20f) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 30f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(30f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_DifferentAttribute_Ignored()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Speed", ModifierOp.Add, 50f) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_MagnitudeScalesModifierValues()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) });
            _controller.ApplyEffect(new EffectSpec(def, magnitude: 2f));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            // magnitude 2 → 10 * 2 = 20, base 100 + 20 = 120
            Assert.That(result, Is.EqualTo(120f).Within(GasTestFixtures.FloatTolerance));
        }

        // ---- ApplyInstant ----

        [Test]
        public void ApplyInstant_Add_ModifiesBaseValue()
        {
            var modifier = GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 25f);

            ModifierAggregator.ApplyInstant(_attributes, modifier, 1f);

            Assert.That(_attributes.Get(_health)!.BaseValue,
                Is.EqualTo(125f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyInstant_Multiply_MultipliesBaseValue()
        {
            var modifier = GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 2f);

            ModifierAggregator.ApplyInstant(_attributes, modifier, 1f);

            Assert.That(_attributes.Get(_health)!.BaseValue,
                Is.EqualTo(200f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyInstant_Override_ReplacesBaseValue()
        {
            var modifier = GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 42f);

            ModifierAggregator.ApplyInstant(_attributes, modifier, 1f);

            Assert.That(_attributes.Get(_health)!.BaseValue,
                Is.EqualTo(42f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyInstant_UnknownAttribute_IsNoOp()
        {
            // Modifier against an attribute the target does not have defined
            var modifier = new Modifier(GasTestFixtures.Tag("Debuff.Burn"), ModifierOp.Add, 500f);

            ModifierAggregator.ApplyInstant(_attributes, modifier, 1f);

            Assert.That(_attributes.Get(_health)!.BaseValue,
                Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
            Assert.That(_attributes.Get(_speed)!.BaseValue,
                Is.EqualTo(10f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ApplyInstant_MagnitudeScalesValue()
        {
            var modifier = GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f);

            ModifierAggregator.ApplyInstant(_attributes, modifier, 3f);

            // 100 + (10 * 3) = 130
            Assert.That(_attributes.Get(_health)!.BaseValue,
                Is.EqualTo(130f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
