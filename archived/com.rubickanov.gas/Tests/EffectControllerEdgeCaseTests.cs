using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class EffectControllerEdgeCaseTests
    {
        private AttributeSet _attributes = null!;
        private GameplayTagContainer _tags = null!;
        private EffectController _controller = null!;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            (_attributes, _tags, _controller) = GasTestFixtures.MakeTargetWithHealth(100f, 10f);
        }

        [TearDown]
        public void TearDown()
        {
            GasTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void Tick_DurationEffectWithZeroPeriod_NoPeriodicApplication()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                period: 0f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 50f) });
            _controller.ApplyEffect(new EffectSpec(def));
            var baseBefore = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue;

            _controller.Tick(1f);
            _controller.Tick(1f);

            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.BaseValue,
                Is.EqualTo(baseBefore).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Tick_DurationZero_RemovesOnFirstTick()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 0f);
            var handle = _controller.ApplyEffect(new EffectSpec(def));

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(1, _controller.ActiveEffects.Count);

            _controller.Tick(0.01f);

            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }

        [Test]
        public void Aggregate_ModifierOrderWithinEffect_DoesNotChangeResult()
        {
            var addThenMul = new[]
            {
                GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f),
                GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 2f)
            };
            var mulThenAdd = new[]
            {
                GasTestFixtures.Mod("Attribute.Health", ModifierOp.Multiply, 2f),
                GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f)
            };

            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f, modifiers: addThenMul)));
            var resultA = ModifierAggregator.Aggregate(100f, GasTestFixtures.Tag("Attribute.Health"), _controller.ActiveEffects);

            _controller.RemoveAllEffects();
            _controller.ApplyEffect(new EffectSpec(GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f, modifiers: mulThenAdd)));
            var resultB = ModifierAggregator.Aggregate(100f, GasTestFixtures.Tag("Attribute.Health"), _controller.ActiveEffects);

            Assert.That(resultA, Is.EqualTo(resultB).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void RemoveEffectsWithTag_InfiniteEffect_Removed()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Infinite, effectTag: "Effect.Buff.Speed");
            _controller.ApplyEffect(new EffectSpec(def));

            var count = _controller.RemoveEffectsWithTag(GasTestFixtures.Tag("Effect.Buff"));

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }

        [Test]
        public void SetBaseValue_WithActiveModifier_RecalculatesCurrentValue()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 10f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 50f) });
            _controller.ApplyEffect(new EffectSpec(def));

            _attributes.SetBaseValue(GasTestFixtures.Tag("Attribute.Health"), 200f);

            Assert.That(_attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!.CurrentValue,
                Is.EqualTo(250f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
