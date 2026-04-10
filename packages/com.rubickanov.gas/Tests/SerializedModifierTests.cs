using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class SerializedModifierTests
    {
        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            GasTestFixtures.EnsureUninstalled();
        }

        private static SerializedModifier FromJson(string attributePath, int operation, float value)
        {
            var json = "{" +
                       $"\"_attribute\":{{\"_path\":\"{attributePath}\"}}," +
                       $"\"_operation\":{operation}," +
                       $"\"_value\":{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                       "}";
            return JsonUtility.FromJson<SerializedModifier>(json);
        }

        [Test]
        public void ToModifier_PopulatedFields_ReturnsMatchingModifier()
        {
            var serialized = FromJson("Attribute.Health", (int)ModifierOp.Add, 25f);

            var modifier = serialized.ToModifier();

            Assert.AreEqual(GasTestFixtures.Tag("Attribute.Health"), modifier.Attribute);
            Assert.AreEqual(ModifierOp.Add, modifier.Operation);
            Assert.That(modifier.Value, Is.EqualTo(25f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ToModifier_UnknownAttributePath_ReturnsModifierWithNoneTag()
        {
            var serialized = FromJson("Does.Not.Exist", (int)ModifierOp.Add, 1f);

            var modifier = serialized.ToModifier();

            Assert.AreEqual(GameplayTags.GameplayTag.None, modifier.Attribute);
        }

        [Test]
        public void ToModifier_OverrideOperation_PropagatesOperation()
        {
            var serialized = FromJson("Attribute.Health", (int)ModifierOp.Override, 42f);

            var modifier = serialized.ToModifier();

            Assert.AreEqual(ModifierOp.Override, modifier.Operation);
            Assert.That(modifier.Value, Is.EqualTo(42f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void Attribute_ExposesSerializedTag()
        {
            var serialized = FromJson("Attribute.Speed", (int)ModifierOp.Multiply, 2f);

            Assert.AreEqual("Attribute.Speed", serialized.Attribute.Path);
            Assert.AreEqual(GasTestFixtures.Tag("Attribute.Speed"), serialized.Attribute.Tag);
        }

        [Test]
        public void Operation_ReturnsStoredEnum()
        {
            var serialized = FromJson("Attribute.Health", (int)ModifierOp.Multiply, 1f);

            Assert.AreEqual(ModifierOp.Multiply, serialized.Operation);
        }

        [Test]
        public void Value_ReturnsStoredFloat()
        {
            var serialized = FromJson("Attribute.Health", (int)ModifierOp.Add, 3.14f);

            Assert.That(serialized.Value, Is.EqualTo(3.14f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
