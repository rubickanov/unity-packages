using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class SpinnerHostTests
    {
        [Test]
        public void Show_AddsClassStyledElementToOverlay()
        {
            var root = TestRoot.Create();
            using var spinner = new SpinnerHost(root);

            using (spinner.Show("Loading"))
            {
                var element = root.Q("overlay-layer").Q(className: SpinnerHost.RootClass);

                Assert.IsNotNull(element);
                Assert.IsNotNull(element.Q(className: SpinnerHost.IconClass));
                Assert.AreEqual("Loading", element.Q<Label>(className: SpinnerHost.LabelClass).text);
            }

            Assert.IsNull(root.Q("overlay-layer").Q(className: SpinnerHost.RootClass));
        }
    }
}
