using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class EffectControllerReentrancyTests
    {
        private AttributeSet _attributes = null!;
        private GameplayTagContainer _tags = null!;
        private EffectController _controller = null!;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            (_attributes, _tags, _controller) = GasTestFixtures.MakeTargetWithHealth();
        }

        [TearDown]
        public void TearDown()
        {
            GasTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void ApplyEffect_DuringEffectApplied_IsAppliedImmediately()
        {
            var inner = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f, effectTag: "Effect.Heal");
            var outer = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f, effectTag: "Effect.Burn");

            bool fired = false;
            _controller.EffectApplied += applied =>
            {
                if (fired) return;
                fired = true;
                if (applied.Def.EffectTag == GasTestFixtures.Tag("Effect.Burn"))
                    _controller.ApplyEffect(new EffectSpec(inner));
            };

            _controller.ApplyEffect(new EffectSpec(outer));

            Assert.AreEqual(2, _controller.ActiveEffects.Count);
        }

        [Test]
        public void RemoveEffect_DuringEffectRemoved_IsRemovedImmediately()
        {
            var a = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f, effectTag: "Effect.Burn");
            var b = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f, effectTag: "Effect.Heal");
            var handleA = _controller.ApplyEffect(new EffectSpec(a));
            var handleB = _controller.ApplyEffect(new EffectSpec(b));

            _controller.EffectRemoved += removed =>
            {
                if (removed.Handle == handleA)
                    _controller.RemoveEffect(handleB);
            };

            _controller.RemoveEffect(handleA);

            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }
    }
}
