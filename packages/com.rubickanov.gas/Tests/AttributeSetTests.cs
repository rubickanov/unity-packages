using System;
using NUnit.Framework;
using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class AttributeSetTests
    {
        private AttributeSet _attributes = null!;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            _attributes = new AttributeSet();
        }

        [TearDown]
        public void TearDown()
        {
            GasTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void Define_NewTag_ReturnsAttributeWithBaseValue()
        {
            var attribute = _attributes.Define(GasTestFixtures.Tag("Attribute.Health"), 100f);

            Assert.IsNotNull(attribute);
            Assert.AreEqual(100f, attribute.BaseValue, GasTestFixtures.FloatTolerance);
            Assert.AreEqual(100f, attribute.CurrentValue, GasTestFixtures.FloatTolerance);
        }

        [Test]
        public void Define_SameTagTwice_Throws()
        {
            var health = GasTestFixtures.Tag("Attribute.Health");
            _attributes.Define(health, 100f);

            Assert.Throws<InvalidOperationException>(() => _attributes.Define(health, 50f));
        }

        [Test]
        public void Get_DefinedTag_ReturnsAttribute()
        {
            var health = GasTestFixtures.Tag("Attribute.Health");
            var defined = _attributes.Define(health, 42f);

            var fetched = _attributes.Get(health);

            Assert.AreSame(defined, fetched);
        }

        [Test]
        public void Get_UndefinedTag_ReturnsNull()
        {
            var result = _attributes.Get(GasTestFixtures.Tag("Attribute.Speed"));

            Assert.IsNull(result);
        }

        [Test]
        public void TryGet_DefinedTag_ReturnsTrueAndAttribute()
        {
            var health = GasTestFixtures.Tag("Attribute.Health");
            var defined = _attributes.Define(health, 42f);

            var found = _attributes.TryGet(health, out var attribute);

            Assert.IsTrue(found);
            Assert.AreSame(defined, attribute);
        }

        [Test]
        public void TryGet_UndefinedTag_ReturnsFalseAndNull()
        {
            var found = _attributes.TryGet(GasTestFixtures.Tag("Attribute.Speed"), out var attribute);

            Assert.IsFalse(found);
            Assert.IsNull(attribute);
        }

        [Test]
        public void SetBaseValue_DefinedTag_UpdatesBaseValueAndFiresEvent()
        {
            var health = GasTestFixtures.Tag("Attribute.Health");
            var attribute = _attributes.Define(health, 100f);

            GameplayTag eventTag = default;
            float eventValue = 0f;
            int callCount = 0;
            _attributes.BaseValueChanged += (tag, value) =>
            {
                eventTag = tag;
                eventValue = value;
                callCount++;
            };

            _attributes.SetBaseValue(health, 250f);

            Assert.AreEqual(250f, attribute.BaseValue, GasTestFixtures.FloatTolerance);
            Assert.AreEqual(1, callCount);
            Assert.AreEqual(health, eventTag);
            Assert.AreEqual(250f, eventValue, GasTestFixtures.FloatTolerance);
        }

        [Test]
        public void SetBaseValue_UndefinedTag_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => _attributes.SetBaseValue(GasTestFixtures.Tag("Attribute.Speed"), 10f));
        }
    }
}
