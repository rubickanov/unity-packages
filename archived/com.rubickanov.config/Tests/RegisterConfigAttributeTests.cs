using System.Reflection;
using NUnit.Framework;

namespace Rubickanov.Config.Tests
{
    [TestFixture]
    public class RegisterConfigAttributeTests
    {
        [Test]
        public void Constructor_StoresAddress()
        {
            var attribute = new RegisterConfigAttribute("foo/bar");

            Assert.AreEqual("foo/bar", attribute.Address);
        }

        [Test]
        public void GetCustomAttribute_OnDecoratedType_ReturnsAttributeWithAddress()
        {
            var attribute = typeof(DecoratedConfig).GetCustomAttribute<RegisterConfigAttribute>();

            Assert.IsNotNull(attribute);
            Assert.AreEqual("Test/Decorated", attribute!.Address);
        }

        [Test]
        public void GetCustomAttribute_OnUndecoratedType_ReturnsNull()
        {
            var attribute = typeof(PlainConfig).GetCustomAttribute<RegisterConfigAttribute>();

            Assert.IsNull(attribute);
        }

        [RegisterConfig("Test/Decorated")]
        private class DecoratedConfig : ConfigBase { }

        private class PlainConfig : ConfigBase { }
    }
}
