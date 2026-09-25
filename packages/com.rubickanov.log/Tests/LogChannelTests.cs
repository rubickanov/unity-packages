using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Rubickanov.Log.Tests
{
    [TestFixture]
    public class LogChannelTests
    {
        // Каналы живут до конца сессии, поэтому каждому тесту — свой.
        private static LogChannel NewChannel() => LogChannel.Get($"Test{Guid.NewGuid():N}");

        [Test]
        public void Get_SameNameInOtherCase_ReturnsSameChannel()
        {
            LogChannel course = NewChannel();

            LogChannel again = LogChannel.Get(course.Name.ToUpperInvariant());

            Assert.AreSame(course, again);
            CollectionAssert.Contains(LogChannel.All, course);
        }

        [TestCase(LogLevel.Verbose, LogLevel.Verbose, true)]
        [TestCase(LogLevel.Info, LogLevel.Verbose, false)]
        [TestCase(LogLevel.Info, LogLevel.Error, true)]
        [TestCase(LogLevel.Off, LogLevel.Error, false)]
        public void Writes_ChannelLevel_WritesItsLevelAndAbove(LogLevel channelLevel, LogLevel messageLevel, bool expected)
        {
            LogChannel course = NewChannel();
            course.Level = channelLevel;

            Assert.AreEqual(expected, course.Writes(messageLevel));
        }

        [Test]
        public void Level_Changed_RaisesLevelChangedOnce()
        {
            LogChannel course = NewChannel();
            course.Level = LogLevel.Info;
            int raised = 0;
            course.LevelChanged += _ => raised++;

            course.Level = LogLevel.Warn;
            course.Level = LogLevel.Warn;

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void Info_ChannelSilent_SkipsInterpolationHoles()
        {
            LogChannel course = NewChannel();
            course.Level = LogLevel.Warn;
            int evaluated = 0;

            course.Info($"Checkpoint {++evaluated}");

            Assert.AreEqual(0, evaluated);
        }

        [Test]
        public void Info_ChannelWrites_FormatsNumbersInvariantly()
        {
            LogChannel course = NewChannel();
            CultureInfo previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape(course.Name) + @".*Run time 12\.5s"));

            try
            {
                course.Info($"Run time {12.5f:F1}s");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void Info_DestroyedObjectInHole_WritesNull()
        {
            LogChannel course = NewChannel();
            var checkpoint = new GameObject("Checkpoint");
            Object.DestroyImmediate(checkpoint);
            LogAssert.Expect(LogType.Log, new Regex("Reached null"));

            course.Info($"Reached {checkpoint}");
        }

        [Test]
        public void Warn_ChannelWrites_LogsWarning()
        {
            LogChannel course = NewChannel();
            LogAssert.Expect(LogType.Warning, new Regex("Player fell out of the course"));

            course.Warn("Player fell out of the course");
        }

        [Test]
        public void Parse_PairsWithSpacesAndCase_ReadsLevels()
        {
            var levels = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);

            LogSettings.Parse("Course = verbose, *=Warn,UI=Off", levels);

            Assert.AreEqual(LogLevel.Verbose, levels["course"]);
            Assert.AreEqual(LogLevel.Warn, levels["*"]);
            Assert.AreEqual(LogLevel.Off, levels["UI"]);
        }

        [Test]
        public void Parse_MalformedPairs_SkipsThem()
        {
            var levels = new Dictionary<string, LogLevel>();

            LogSettings.Parse("Course,UI=Loud,Audio=Error=Warn,,Net=Error", levels);

            Assert.AreEqual(1, levels.Count);
            Assert.AreEqual(LogLevel.Error, levels["Net"]);
        }
    }
}
