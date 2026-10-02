using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.Log;

namespace Rubickanov.DevConsole.Log.Tests
{
    [TestFixture]
    public class LogChannelCommandsTests
    {
        // Channels are process-wide and never go away, so each test makes its own and puts every level back
        private readonly Dictionary<LogChannel, LogLevel> _levels = new();
        private LogChannel _channel = null!;

        [SetUp]
        public void SetUp()
        {
            foreach (var channel in LogChannel.All) _levels[channel] = channel.Level;
            _channel = LogChannel.Get("DevConsoleTest" + Guid.NewGuid().ToString("N"));
            _channel.Level = LogLevel.Info;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _levels) pair.Key.Level = pair.Value;
            _levels.Clear();
            _channel.Level = LogLevel.Info;
        }

        [Test]
        public void SetLevel_NamedChannel_ChangesOnlyThatChannel()
        {
            var other = LogChannel.Get("DevConsoleTestOther");
            var otherBefore = other.Level;

            LogChannelCommands.SetLevel(_channel.Name, LogLevel.Verbose);

            Assert.AreEqual(LogLevel.Verbose, _channel.Level);
            Assert.AreEqual(otherBefore, other.Level);
        }

        [Test]
        public void SetLevel_Star_ChangesEveryChannel()
        {
            LogChannelCommands.SetLevel("*", LogLevel.Error);

            foreach (var channel in LogChannel.All)
                Assert.AreEqual(LogLevel.Error, channel.Level, channel.Name);
        }

        [Test]
        public void SetLevel_NoLevel_ShowsTheCurrentOneAndChangesNothing()
        {
            var message = LogChannelCommands.SetLevel(_channel.Name);

            StringAssert.Contains("Info", message);
            Assert.AreEqual(LogLevel.Info, _channel.Level);
        }

        [Test]
        public void SetLevel_StarWithoutLevel_Fails()
        {
            Assert.Throws<CommandException>(() => LogChannelCommands.SetLevel("*"));
        }

        [Test]
        public void Execute_LogLevelTypedInTheConsole_ParsesTheLevelCaseInsensitively()
        {
            var registry = new CommandRegistry();
            registry.Initialize();

            var result = registry.Execute($"log level {_channel.Name} warn");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(LogLevel.Warn, _channel.Level);
        }

        [Test]
        public void Provider_Partial_SuggestsMatchingChannels()
        {
            var results = new List<string>();

            new LogChannelProvider().GetSuggestions("devconsoletest" + _channel.Name.Substring(14, 4), results);

            CollectionAssert.Contains(results, _channel.Name);
            CollectionAssert.DoesNotContain(results, "*");
        }

        [Test]
        public void Provider_Empty_SuggestsStarFirst()
        {
            var results = new List<string>();

            new LogChannelProvider().GetSuggestions("", results);

            Assert.AreEqual("*", results[0]);
            CollectionAssert.Contains(results, _channel.Name);
        }

        [Test]
        public void GetSuggestions_LogLevelChannelTyped_SuggestsTheLevels()
        {
            var registry = new CommandRegistry();
            registry.Initialize();
            var results = new List<string>();

            registry.GetSuggestions($"log level {_channel.Name} ", results);

            CollectionAssert.AreEquivalent(Enum.GetNames(typeof(LogLevel)), results);
        }
    }
}
