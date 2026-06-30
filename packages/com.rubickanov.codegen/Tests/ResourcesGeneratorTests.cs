using NUnit.Framework;
using Rubickanov.Codegen.Editor.Generators;

namespace Rubickanov.Codegen.Tests
{
    [TestFixture]
    public class ResourcesGeneratorTests
    {
        [Test]
        public void ToResourcesPath_UnderResources_StripsPrefixAndExtension()
        {
            var result = ResourcesGenerator.ToResourcesPath("Assets/Game/Resources/UI/MainMenu.prefab");

            Assert.AreEqual("UI/MainMenu", result);
        }

        [Test]
        public void ToResourcesPath_RootOfResources_ReturnsBareName()
        {
            var result = ResourcesGenerator.ToResourcesPath("Assets/Resources/Config.asset");

            Assert.AreEqual("Config", result);
        }

        [Test]
        public void ToResourcesPath_NotUnderResources_ReturnsNull()
        {
            var result = ResourcesGenerator.ToResourcesPath("Assets/Game/UI/MainMenu.prefab");

            Assert.IsNull(result);
        }

        [Test]
        public void ToResourcesPath_NestedResources_UsesDeepestRoot()
        {
            var result = ResourcesGenerator.ToResourcesPath("Assets/Resources/Sub/Resources/Foo.asset");

            Assert.AreEqual("Foo", result);
        }
    }
}
