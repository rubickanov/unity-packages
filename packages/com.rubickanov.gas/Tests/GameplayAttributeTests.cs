using NUnit.Framework;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class GameplayAttributeTests
    {
        [Test]
        public void Constructor_DefaultBaseValue_IsZero()
        {
            var attribute = new GameplayAttribute();

            Assert.AreEqual(0f, attribute.BaseValue, GasTestFixtures.FloatTolerance);
            Assert.AreEqual(0f, attribute.CurrentValue, GasTestFixtures.FloatTolerance);
        }

        [Test]
        public void Constructor_WithBaseValue_SetsBaseAndCurrent()
        {
            var attribute = new GameplayAttribute(100f);

            Assert.AreEqual(100f, attribute.BaseValue, GasTestFixtures.FloatTolerance);
            Assert.AreEqual(100f, attribute.CurrentValue, GasTestFixtures.FloatTolerance);
        }

        [Test]
        public void CurrentValue_InitiallyEqualsBaseValue()
        {
            var attribute = new GameplayAttribute(42f);

            Assert.AreEqual(attribute.BaseValue, attribute.CurrentValue, GasTestFixtures.FloatTolerance);
        }
    }
}
