using System.IO;
using NUnit.Framework;
using Rubickanov.Codegen.Editor;

namespace Rubickanov.Codegen.Tests
{
    [TestFixture]
    public class GeneratedFileWriterTests
    {
        private string _directory = string.Empty;
        private string _filePath = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "RubickanovCodegenTests_" + Path.GetRandomFileName());
            _filePath = Path.Combine(_directory, "Generated.cs");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Write_NewFile_ReturnsTrueAndWritesContent()
        {
            var changed = GeneratedFileWriter.Write(_filePath, "hello");

            Assert.IsTrue(changed);
            Assert.AreEqual("hello", File.ReadAllText(_filePath));
        }

        [Test]
        public void Write_UnchangedContent_ReturnsFalse()
        {
            GeneratedFileWriter.Write(_filePath, "hello");

            var changed = GeneratedFileWriter.Write(_filePath, "hello");

            Assert.IsFalse(changed);
        }

        [Test]
        public void Write_ChangedContent_ReturnsTrueAndOverwrites()
        {
            GeneratedFileWriter.Write(_filePath, "hello");

            var changed = GeneratedFileWriter.Write(_filePath, "world");

            Assert.IsTrue(changed);
            Assert.AreEqual("world", File.ReadAllText(_filePath));
        }

        [Test]
        public void Write_MissingDirectory_IsCreated()
        {
            GeneratedFileWriter.Write(_filePath, "hello");

            Assert.IsTrue(Directory.Exists(_directory));
        }
    }
}
