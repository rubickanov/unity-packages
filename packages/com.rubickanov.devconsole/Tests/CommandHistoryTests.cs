using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class CommandHistoryTests
    {
        private const string LegacyPrefsKey = "DevConsole_History";
        private string _config = null!;
        private CommandHistory _history = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            // The sandbox's own 3.x history would otherwise be read in as this test's
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
            _history = new CommandRegistry().History;
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
            TestConfig.Release(_config);
        }

        [Test]
        public void Add_PastTheCap_DropsTheOldest()
        {
            for (int i = 0; i <= CommandHistory.MaxEntries; i++) _history.Add($"spawn crate {i}");

            Assert.AreEqual(CommandHistory.MaxEntries, _history.Entries.Count);
            Assert.AreEqual("spawn crate 1", _history.Entries[0]);
            Assert.AreEqual($"spawn crate {CommandHistory.MaxEntries}", _history.Entries[^1]);
        }

        [Test]
        public void Add_SameLineTwiceInARow_KeepsOne()
        {
            _history.Add("timescale 0.5");
            _history.Add("timescale 0.5");

            Assert.AreEqual(1, _history.Entries.Count);
        }

        [Test]
        public void Add_BlankLine_IsIgnored()
        {
            _history.Add("   ");

            Assert.AreEqual(0, _history.Entries.Count);
        }

        [Test]
        public void NavigateUpThenDown_PastTheNewest_GivesBackWhatWasBeingTyped()
        {
            _history.Add("god true");
            _history.Add("timescale 0.5");

            var newest = _history.NavigateUp("tele");
            var older = _history.NavigateUp("timescale 0.5");
            var stays = _history.NavigateUp("god true");
            _history.NavigateDown();
            var typed = _history.NavigateDown();

            Assert.AreEqual("timescale 0.5", newest);
            Assert.AreEqual("god true", older);
            Assert.AreEqual("god true", stays);
            Assert.AreEqual("tele", typed);
        }

        [Test]
        public void History_NextSession_ReadsTheSameLines()
        {
            _history.Add("god true");
            _history.Add("teleport \"spawn point\"");

            var next = new CommandRegistry().History;

            CollectionAssert.AreEqual(new[] { "god true", "teleport \"spawn point\"" }, next.Entries);
        }

        [Test]
        public void History_IsAPlainFileBesideTheConfig()
        {
            _history.Add("god true");

            Assert.AreEqual(Path.Combine(_config, "history.txt"), CommandHistory.FilePath);
            Assert.AreEqual("god true", File.ReadAllText(CommandHistory.FilePath).Trim());
        }

        [Test]
        public void Load_FileLongerThanTheCap_KeepsTheNewest()
        {
            var lines = new string[CommandHistory.MaxEntries + 20];
            for (int i = 0; i < lines.Length; i++) lines[i] = $"echo {i}";
            File.WriteAllLines(CommandHistory.FilePath, lines);

            var history = new CommandRegistry().History;

            Assert.AreEqual(CommandHistory.MaxEntries, history.Entries.Count);
            Assert.AreEqual("echo 20", history.Entries[0]);
        }

        [Test]
        public void Load_PlayerPrefsHistoryOf3x_MovesIntoTheFile()
        {
            PlayerPrefs.SetString(LegacyPrefsKey, "{\"commands\":[\"god true\",\"fps\"]}");

            var history = new CommandRegistry().History;
            history.Add("memory");

            CollectionAssert.AreEqual(new[] { "god true", "fps", "memory" }, history.Entries);
            Assert.IsTrue(File.Exists(CommandHistory.FilePath));
            Assert.IsFalse(PlayerPrefs.HasKey(LegacyPrefsKey));
        }

        [Test]
        public void Clear_RemovesTheLinesAndTheFile()
        {
            _history.Add("god true");

            _history.Clear();

            Assert.AreEqual(0, _history.Entries.Count);
            Assert.IsFalse(File.Exists(CommandHistory.FilePath));
        }

        [Test]
        public void HistoryCommands_WithoutAWindow_Work()
        {
            var registry = new CommandRegistry();
            Commands.ConsoleCommands.Register(registry);
            registry.History.Add("god true");

            var list = registry.Execute("history list");
            var clear = registry.Execute("history clear");

            Assert.IsTrue(list.Success, list.Message);
            Assert.IsTrue(clear.Success, clear.Message);
            Assert.AreEqual(0, registry.History.Entries.Count);
        }
    }
}
