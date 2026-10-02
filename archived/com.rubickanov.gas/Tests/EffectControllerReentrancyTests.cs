using System.Collections.Generic;
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

        [Test]
        public void ApplyEffect_FromValueChangedHandler_DoesNotThrowAndApplies()
        {
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"));
            var reentrant = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Speed", ModifierOp.Add, 5f) },
                effectTag: "Effect.Buff");

            bool fired = false;
            health!.ValueChanged += (_, _) =>
            {
                if (fired) return;
                fired = true;
                _controller.ApplyEffect(new EffectSpec(reentrant));
            };

            var damage = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, -10f) },
                effectTag: "Effect.Burn");

            Assert.DoesNotThrow(() => _controller.ApplyEffect(new EffectSpec(damage)));
            Assert.AreEqual(2, _controller.ActiveEffects.Count);
        }

        [Test]
        public void RemoveEffect_FromValueChangedHandler_DoesNotThrow()
        {
            var buff = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Speed", ModifierOp.Add, 5f) },
                effectTag: "Effect.Buff");
            var buffHandle = _controller.ApplyEffect(new EffectSpec(buff));

            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"));
            bool fired = false;
            health!.ValueChanged += (_, _) =>
            {
                if (fired) return;
                fired = true;
                _controller.RemoveEffect(buffHandle);
            };

            var damage = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, -10f) },
                effectTag: "Effect.Burn");

            Assert.DoesNotThrow(() => _controller.ApplyEffect(new EffectSpec(damage)));
            Assert.AreEqual(1, _controller.ActiveEffects.Count);
            Assert.AreEqual(GasTestFixtures.Tag("Effect.Burn"), _controller.ActiveEffects[0].Def.EffectTag);
        }

        [Test]
        public void ApplyEffect_RemovingExistingWhileValueChangedReenters_StillFiresEffectRemoved()
        {
            var existing = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f, effectTag: "Effect.Heal");
            _controller.ApplyEffect(new EffectSpec(existing));

            var removedTags = new List<GameplayTag>();
            _controller.EffectRemoved += e => removedTags.Add(e.Def.EffectTag);

            var reentrant = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Speed", ModifierOp.Add, 1f) },
                effectTag: "Effect.Buff");
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"));
            bool fired = false;
            health!.ValueChanged += (_, _) =>
            {
                if (fired) return;
                fired = true;
                _controller.ApplyEffect(new EffectSpec(reentrant));
            };

            var incoming = GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, -10f) },
                removeEffectsWithTags: new[] { "Effect.Heal" },
                effectTag: "Effect.Burn");

            _controller.ApplyEffect(new EffectSpec(incoming));

            Assert.Contains(GasTestFixtures.Tag("Effect.Heal"), removedTags);
        }

        [Test]
        public void Dispose_DetachesFromBaseValueChanged()
        {
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"));
            int valueChanges = 0;
            health!.ValueChanged += (_, _) => valueChanges++;

            _controller.Dispose();
            _attributes.SetBaseValue(GasTestFixtures.Tag("Attribute.Health"), 50f);

            Assert.AreEqual(0, valueChanges);
        }
    }
}
