using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class EffectControllerRemovalTests
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

        // ---- RemoveEffect ----

        [Test]
        public void RemoveEffect_ValidHandle_RemovesFromList()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 5f);
            var handle = _controller.ApplyEffect(new EffectSpec(def));

            var removed = _controller.RemoveEffect(handle);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }

        [Test]
        public void RemoveEffect_ValidHandle_FiresEffectRemoved()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 5f);
            var handle = _controller.ApplyEffect(new EffectSpec(def));
            _removed.Clear();

            _controller.RemoveEffect(handle);

            Assert.AreEqual(1, _removed.Count);
            Assert.AreEqual(handle, _removed[0].Handle);
        }

        [Test]
        public void RemoveEffect_InvalidHandle_ReturnsZero()
        {
            var removed = _controller.RemoveEffect(ActiveEffectHandle.Invalid);

            Assert.AreEqual(0, removed);
        }

        [Test]
        public void RemoveEffect_UnknownHandle_ReturnsZero()
        {
            var def = GasTestFixtures.MakeEffect(DurationPolicy.Duration, durationSeconds: 5f);
            var knownHandle = _controller.ApplyEffect(new EffectSpec(def));
            _controller.RemoveEffect(knownHandle);

            var removed = _controller.RemoveEffect(knownHandle);

            Assert.AreEqual(0, removed);
        }

        [Test]
        public void RemoveEffect_RevokesGrantedTags()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                grantedTags: new[] { "Status.Stun" });
            var handle = _controller.ApplyEffect(new EffectSpec(def));
            Assert.IsTrue(_tags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));

            _controller.RemoveEffect(handle);

            Assert.IsFalse(_tags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));
        }

        [Test]
        public void RemoveEffect_DoesNotRevokeTagGrantedByOtherActiveEffect()
        {
            var defA = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                grantedTags: new[] { "Status.Stun" });
            var defB = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                grantedTags: new[] { "Status.Stun" });
            var handleA = _controller.ApplyEffect(new EffectSpec(defA));
            _controller.ApplyEffect(new EffectSpec(defB));

            _controller.RemoveEffect(handleA);

            // Second effect still grants Status.Stun
            Assert.IsTrue(_tags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));
        }

        [Test]
        public void RemoveEffect_RecalculatesAttributes()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration,
                durationSeconds: 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 50f) });
            var handle = _controller.ApplyEffect(new EffectSpec(def));
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;
            Assert.That(health.CurrentValue, Is.EqualTo(150f).Within(GasTestFixtures.FloatTolerance));

            _controller.RemoveEffect(handle);

            Assert.That(health.CurrentValue, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
        }

        // ---- RemoveEffectsWithTag ----

        [Test]
        public void RemoveEffectsWithTag_ExactMatch_RemovesMatching()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Burn");
            _controller.ApplyEffect(new EffectSpec(def));

            var count = _controller.RemoveEffectsWithTag(GasTestFixtures.Tag("Effect.Burn"));

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }

        [Test]
        public void RemoveEffectsWithTag_HierarchyMatch_RemovesDescendants()
        {
            // Effect has EffectTag "Debuff.Burn". Query "Debuff" → Debuff.Burn.Matches(Debuff) = true.
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Debuff.Burn");
            _controller.ApplyEffect(new EffectSpec(def));

            var count = _controller.RemoveEffectsWithTag(GasTestFixtures.Tag("Debuff"));

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }

        [Test]
        public void RemoveEffectsWithTag_NoMatches_ReturnsZero()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Burn");
            _controller.ApplyEffect(new EffectSpec(def));

            var count = _controller.RemoveEffectsWithTag(GasTestFixtures.Tag("Effect.Heal"));

            Assert.AreEqual(0, count);
            Assert.AreEqual(1, _controller.ActiveEffects.Count);
        }

        [Test]
        public void RemoveEffectsWithTag_InvalidTag_ReturnsZero()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                effectTag: "Effect.Burn");
            _controller.ApplyEffect(new EffectSpec(def));

            var count = _controller.RemoveEffectsWithTag(GameplayTag.None);

            Assert.AreEqual(0, count);
            Assert.AreEqual(1, _controller.ActiveEffects.Count);
        }

        // ---- RemoveAllEffects ----

        [Test]
        public void RemoveAllEffects_EmptiesActiveList()
        {
            _controller.ApplyEffect(new EffectSpec(
                GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f, effectTag: "Effect.Burn")));
            _controller.ApplyEffect(new EffectSpec(
                GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f, effectTag: "Effect.Heal")));
            _controller.ApplyEffect(new EffectSpec(
                GasTestFixtures.MakeEffect(DurationPolicy.Infinite, effectTag: "Effect.Buff.Speed")));

            _controller.RemoveAllEffects();

            Assert.AreEqual(0, _controller.ActiveEffects.Count);
        }

        [Test]
        public void RemoveAllEffects_FiresEffectRemovedPerEffect()
        {
            _controller.ApplyEffect(new EffectSpec(
                GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f)));
            _controller.ApplyEffect(new EffectSpec(
                GasTestFixtures.MakeEffect(DurationPolicy.Duration, 5f)));
            _controller.ApplyEffect(new EffectSpec(
                GasTestFixtures.MakeEffect(DurationPolicy.Infinite)));
            _removed.Clear();

            _controller.RemoveAllEffects();

            Assert.AreEqual(3, _removed.Count);
        }

        [Test]
        public void RemoveAllEffects_RecalculatesAttributes()
        {
            var def = GasTestFixtures.MakeEffect(
                DurationPolicy.Duration, 5f,
                modifiers: new[] { GasTestFixtures.Mod("Attribute.Health", ModifierOp.Add, 50f) });
            _controller.ApplyEffect(new EffectSpec(def));
            var health = _attributes.Get(GasTestFixtures.Tag("Attribute.Health"))!;
            Assert.That(health.CurrentValue, Is.EqualTo(150f).Within(GasTestFixtures.FloatTolerance));

            _controller.RemoveAllEffects();

            Assert.That(health.CurrentValue, Is.EqualTo(100f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
