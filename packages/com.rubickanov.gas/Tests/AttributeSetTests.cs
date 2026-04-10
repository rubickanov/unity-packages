using NUnit.Framework;

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
        public void Define_SameTagTwice_ReturnsSameInstance()
        {
            var health = GasTestFixtures.Tag("Attribute.Health");
            var first = _attributes.Define(health, 100f);
            var second = _attributes.Define(health, 50f);

            Assert.AreSame(first, second);
        }

        [Test]
        public void Define_SameTagDifferentBaseValue_IgnoresSecondBaseValue()
        {
            var health = GasTestFixtures.Tag("Attribute.Health");
            var first = _attributes.Define(health, 100f);
            _attributes.Define(health, 999f);

            Assert.AreEqual(100f, first.BaseValue, GasTestFixtures.FloatTolerance);
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
    }
}
