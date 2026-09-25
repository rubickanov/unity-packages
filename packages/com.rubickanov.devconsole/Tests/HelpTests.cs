using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class HelpTests
    {
        private CommandRegistry _registry = null!;
        private string _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            // Initialize also discovers the built-in attribute commands, which is what help is asked about here
            _registry = new CommandRegistry();
            _registry.Initialize();
            ConsoleLog.Clear();
        }

        [TearDown]
        public void TearDown() => TestConfig.Release(_config);

        private static string Logged()
        {
            var lines = new List<string>();
            foreach (var entry in ConsoleLog.Entries) lines.Add(entry.Message);
            return string.Join("\n", lines);
        }

        [Test]
        public void Help_Category_ListsOnlyThatCategory()
        {
            Assert.IsTrue(_registry.Execute("help rendering").Success);

            StringAssert.Contains("quality", Logged());
            StringAssert.DoesNotContain("memory", Logged());
        }

        [Test]
        public void Help_Text_SearchesNamesAndDescriptions()
        {
            Assert.IsTrue(_registry.Execute("help garbage").Success);

            StringAssert.Contains("gc", Logged());
            StringAssert.DoesNotContain("quality", Logged());
        }

        [Test]
        public void Help_NothingMatches_IsAnError()
        {
            var result = _registry.Execute("help zzzqqq");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("zzzqqq", result.Message ?? "");
        }

        [Test]
        public void Help_Alias_SaysWhatItRuns()
        {
            AliasRegistry.Instance.Set("slow", "timescale 0.2");

            _registry.Execute("help slow");

            StringAssert.Contains("timescale 0.2", Logged());
        }

        [Test]
        public void Help_Suggestions_IncludeCategories()
        {
            var results = new List<string>();

            _registry.GetSuggestions("help Ren", results);

            CollectionAssert.Contains(results, "Rendering");
        }
    }
}
