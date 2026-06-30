using NUnit.Framework;
using Rubickanov.Codegen.Editor.Generators;

namespace Rubickanov.Codegen.Tests
{
    [TestFixture]
    public class UIToolkitNamesGeneratorTests
    {
        private const string Uxml =
            "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
            "  <ui:Button name=\"reload-btn\" class=\"hud-btn\" />" +
            "  <ui:Label name=\"hp\" />" +
            "  <ui:VisualElement />" +
            "</ui:UXML>";

        [Test]
        public void ExtractElementNames_NamedElements_ReturnedInDocumentOrder()
        {
            var names = UIToolkitNamesGenerator.ExtractElementNames(Uxml);

            Assert.AreEqual(new[] { "reload-btn", "hp" }, names);
        }

        [Test]
        public void ExtractElementNames_UnnamedElements_Ignored()
        {
            var names = UIToolkitNamesGenerator.ExtractElementNames(Uxml);

            Assert.AreEqual(2, names.Count);
        }

        [Test]
        public void ExtractElementNames_DuplicateNames_Deduplicated()
        {
            var uxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "  <ui:Button name=\"x\" />" +
                "  <ui:Button name=\"x\" />" +
                "</ui:UXML>";

            var names = UIToolkitNamesGenerator.ExtractElementNames(uxml);

            Assert.AreEqual(new[] { "x" }, names);
        }

        [Test]
        public void ExtractElementNames_EmptyInput_ReturnsEmpty()
        {
            var names = UIToolkitNamesGenerator.ExtractElementNames("");

            Assert.IsEmpty(names);
        }
    }
}
