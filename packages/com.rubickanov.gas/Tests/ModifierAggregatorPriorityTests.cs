using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class ModifierAggregatorPriorityTests
    {
        private AttributeSet _attributes = null!;
        private GameplayTagContainer _tags = null!;
        private EffectController _controller = null!;
        private GameplayTag _health;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            (_attributes, _tags, _controller) = GasTestFixtures.MakeTargetWithHealth(100f, 10f);
            _health = GasTestFixtures.Tag("Attribute.Health");
        }

        [TearDown]
        public void TearDown()
        {
            GasTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void Aggregate_MultipleOverrides_HigherPriorityWins()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 10f, priority: 100) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 20f, priority: 0) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(10f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_MultipleOverrides_TiedPriorityLastApplied_Wins()
        {
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 10f, priority: 5) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 20f, priority: 5) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(20f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Aggregate_ImmunityWithPriority_BeatsDebuff_RegardlessOfOrder()
        {
            // Immunity should win over Stun (Override 0) when it has higher priority.
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 0f, priority: 0) })));
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Override, 999f, priority: 100) })));

            var result = ModifierAggregator.Aggregate(100f, _health, _controller.ActiveEffects);

            Assert.That(result, Is.EqualTo(999f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
