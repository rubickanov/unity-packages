using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Reactive;

namespace Rubickanov.ACS.Runtime.Reactive.Tests
{
    /// <summary>
    /// Edit-mode tests for <see cref="ComputedProperty"/>. Pure R3 — no Unity types, no aspects,
    /// so they exercise the recompute and disposal contract in isolation.
    /// </summary>
    [TestFixture]
    public class ComputedPropertyTests
    {
        [Test]
        public void From_SingleSource_SeedsInitialValueFromSource()
        {
            var hp = new ReactiveProperty<int>(100);

            using var doubled = ComputedProperty.From(hp, h => h * 2);

            Assert.AreEqual(200, doubled.CurrentValue);
        }

        [Test]
        public void From_SingleSource_RecomputesWhenSourceChanges()
        {
            var hp = new ReactiveProperty<int>(100);
            using var doubled = ComputedProperty.From(hp, h => h * 2);

            hp.Value = 10;

            Assert.AreEqual(20, doubled.CurrentValue);
        }

        [Test]
        public void From_TwoSources_RecomputesWhenEitherSourceChanges()
        {
            var hp = new ReactiveProperty<float>(50f);
            var max = new ReactiveProperty<float>(100f);
            using var percent = ComputedProperty.From(hp, max, (h, m) => m > 0f ? h / m : 0f);

            Assert.AreEqual(0.5f, percent.CurrentValue, 1e-5f);

            max.Value = 200f;
            Assert.AreEqual(0.25f, percent.CurrentValue, 1e-5f);

            hp.Value = 100f;
            Assert.AreEqual(0.5f, percent.CurrentValue, 1e-5f);
        }

        [Test]
        public void Property_DeliversCurrentValueOnSubscribeAndOnChange()
        {
            var hp = new ReactiveProperty<int>(1);
            using var doubled = ComputedProperty.From(hp, h => h * 2);

            int observed = 0;
            using var sub = doubled.Property.Subscribe(v => observed = v);
            Assert.AreEqual(2, observed);

            hp.Value = 5;
            Assert.AreEqual(10, observed);
        }

        [Test]
        public void Dispose_StopsPushingToSubscribers()
        {
            var hp = new ReactiveProperty<int>(1);
            var doubled = ComputedProperty.From(hp, h => h * 2);

            int emissions = 0;
            using var sub = doubled.Property.Subscribe(_ => emissions++);
            int afterSubscribe = emissions;

            doubled.Dispose();
            hp.Value = 999;

            Assert.AreEqual(afterSubscribe, emissions);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var hp = new ReactiveProperty<int>(1);
            var doubled = ComputedProperty.From(hp, h => h * 2);

            doubled.Dispose();

            Assert.DoesNotThrow(() => doubled.Dispose());
        }
    }
}
