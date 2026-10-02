using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class EffectControllerStackingTests
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
        public void Stacking_Independent_SameEffectTag_CoexistsInActiveList()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Burn",
                stacking: StackingPolicy.Independent);

            _controller.ApplyEffect(new EffectSpec(def));
            _controller.ApplyEffect(new EffectSpec(def));
            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(3, _controller.ActiveEffects.Count);
        }

        [Test]
        public void Stacking_Replace_SameEffectTag_RemovesPriorInstance()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Burn",
                stacking: StackingPolicy.Replace);

            _controller.ApplyEffect(new EffectSpec(def));
            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(1, _controller.ActiveEffects.Count);
        }

        [Test]
        public void Stacking_Replace_FiresEffectRemovedForPrior()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Burn",
                stacking: StackingPolicy.Replace);

            var firstHandle = _controller.ApplyEffect(new EffectSpec(def));
            _removed.Clear();

            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(1, _removed.Count);
            Assert.AreEqual(firstHandle, _removed[0].Handle);
        }

        [Test]
        public void Stacking_Replace_DifferentEffectTag_DoesNotRemove()
        {
            var burnDef = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Burn",
                stacking: StackingPolicy.Replace);
            var healDef = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Heal",
                stacking: StackingPolicy.Replace);

            _controller.ApplyEffect(new EffectSpec(burnDef));
            _controller.ApplyEffect(new EffectSpec(healDef));

            Assert.AreEqual(2, _controller.ActiveEffects.Count);
        }

        [Test]
        public void Stacking_Replace_EffectTagNone_DoesNotDedupe()
        {
            // When EffectTag is None, Replace stacking should not dedupe — it needs a valid tag to match.
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: null,
                stacking: StackingPolicy.Replace);

            _controller.ApplyEffect(new EffectSpec(def));
            _controller.ApplyEffect(new EffectSpec(def));

            Assert.AreEqual(2, _controller.ActiveEffects.Count);
        }

        [Test]
        public void Stacking_Replace_RecalculatesAttributesAfterReplacement()
        {
            var weakDef = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 10f) },
                effectTag: "Effect.Buff",
                stacking: StackingPolicy.Replace);
            var strongDef = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 50f) },
                effectTag: "Effect.Buff",
                stacking: StackingPolicy.Replace);

            _controller.ApplyEffect(new EffectSpec(weakDef));
            _controller.ApplyEffect(new EffectSpec(strongDef));
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;

            Assert.That(health.CurrentValue, Is.EqualTo(150f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
