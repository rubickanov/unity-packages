using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class RichTextTests
    {
        [Test]
        public void Strip_RemovesUnityTags()
        {
            Assert.AreEqual("[Net] hello",
                RichText.Strip("<b><color=#FF8800>[Net]</color></b> <color=#8C8C8C>hello</color>"));
        }

        [Test]
        public void Strip_KeepsTextThatOnlyLooksLikeTags()
        {
            Assert.AreEqual("List<int> a < b > c <none>", RichText.Strip("List<int> a < b > c <none>"));
        }

        [Test]
        public void Strip_IgnoresTagCase()
        {
            Assert.AreEqual("x", RichText.Strip("<B><Size=20>x</size></B>"));
        }

        [Test]
        public void KeepColor_RemovesEverythingButColour()
        {
            Assert.AreEqual("<color=#FF8800>[Net]</color> hi",
                RichText.KeepColor("<b><color=#FF8800>[Net]</color></b> <i>hi</i>"));
        }

        [Test]
        public void Strip_NullAndEmpty_ReturnEmpty()
        {
            Assert.AreEqual("", RichText.Strip(null!));
            Assert.AreEqual("", RichText.Strip(""));
        }
    }
}
