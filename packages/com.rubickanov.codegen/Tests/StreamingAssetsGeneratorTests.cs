using NUnit.Framework;
using Rubickanov.Codegen.Editor.Generators;

namespace Rubickanov.Codegen.Tests
{
    [TestFixture]
    public class StreamingAssetsGeneratorTests
    {
        [Test]
        public void ToRelativePath_NestedFile_StripsRootAndKeepsExtension()
        {
            var result = StreamingAssetsGenerator.ToRelativePath(
                "/proj/Assets/StreamingAssets", "/proj/Assets/StreamingAssets/config/data.json");

            Assert.AreEqual("config/data.json", result);
        }

        [Test]
        public void ToRelativePath_BackslashSeparators_Normalized()
        {
            var result = StreamingAssetsGenerator.ToRelativePath(
                @"C:\proj\Assets\StreamingAssets", @"C:\proj\Assets\StreamingAssets\audio\theme.ogg");

            Assert.AreEqual("audio/theme.ogg", result);
        }

        [Test]
        public void ToRelativePath_RootFile_ReturnsBareName()
        {
            var result = StreamingAssetsGenerator.ToRelativePath(
                "/proj/Assets/StreamingAssets", "/proj/Assets/StreamingAssets/manifest.json");

            Assert.AreEqual("manifest.json", result);
        }
    }
}
