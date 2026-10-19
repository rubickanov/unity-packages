using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.Tests.Data
{
    /// <summary>
    /// A system writes over a copy of its base: what it does not know survives, what it removes stays removed once the
    /// result is the next base; a reader falls back on values of the wrong kind and says where.
    /// </summary>
    public sealed class SaveWriterTests
    {
        // Written by a newer build: "rain", "future" and Ann's "mood" are unknown to the code below.
        private const string NewerText = @"{
            ""clock"": { ""day"": 3, ""rain"": true },
            ""future"": [1, 2],
            ""kept"": {
                ""ann"": { ""name"": ""Ann"", ""mood"": ""glad"" },
                ""bob"": { ""name"": ""Bob"" }
            }
        }";

        [Test]
        public void Over_UnknownKeysAndEntryFields_SurviveTheRewrite()
        {
            SaveNode baseTree = Read(NewerText);

            SaveWriter w = SaveWriter.Over(baseTree);
            w.Object("clock").Int("day", 4);
            w.Map("kept").Entry("ann").String("name", "Ann B");

            SaveNode tree = w.Tree;
            Assert.That(tree.At("clock/day").Long, Is.EqualTo(4));
            Assert.That(tree.At("clock/rain").Bool, Is.True);
            Assert.That(tree["future"].Count, Is.EqualTo(2));
            Assert.That(tree.At("kept/ann/name").String, Is.EqualTo("Ann B"));
            Assert.That(tree.At("kept/ann/mood").String, Is.EqualTo("glad"));
            Assert.That(tree.At("kept/bob"), Is.Null, "an entry not written is gone");
            Assert.That(baseTree.At("clock/day").Long, Is.EqualTo(3), "the base itself is not changed");
        }

        [Test]
        public void Over_TheReturnedTreeAsTheNextBase_KeepsRemovedKeysRemoved()
        {
            SaveWriter first = SaveWriter.Over(Read(NewerText));
            first.Remove("future");
            first.Map("kept").Entry("ann");

            SaveWriter second = SaveWriter.Over(first.Tree);
            second.Object("clock").Int("day", 5);
            second.Map("kept").Entry("ann");

            Assert.That(second.Tree["future"], Is.Null);
            Assert.That(second.Tree.At("kept/bob"), Is.Null);
            Assert.That(second.Tree.At("kept/ann/mood").String, Is.EqualTo("glad"));
        }

        [Test]
        public void Read_ValuesOfTheWrongKind_FallBackWithANoteAtTheirPath()
        {
            SaveNode tree = Read(@"{ ""clock"": { ""day"": ""three"", ""hour"": 3000000000, ""sky"": ""Snow"" } }");
            var notes = new List<string>();
            SaveReader clock = SaveReader.Of(tree, notes).Object("clock");

            int day = clock.Int("day", 1);
            int hour = clock.Int("hour", 8);
            Weather sky = clock.Enum("sky", Weather.Clear);
            float wind = clock.Float("wind", 0.5f);

            Assert.That(day, Is.EqualTo(1));
            Assert.That(hour, Is.EqualTo(8));
            Assert.That(sky, Is.EqualTo(Weather.Clear));
            Assert.That(wind, Is.EqualTo(0.5f), "a missing key falls back quietly");
            Assert.That(notes, Is.EqualTo(new[]
            {
                "clock/day: a number expected, \"three\" found",
                "clock/hour: an int expected, 3000000000 found",
                "clock/sky: a Weather expected, \"Snow\" found",
            }));
        }

        private static SaveNode Read(string text)
        {
            Assert.That(SaveJson.TryRead(text, out SaveNode node, out string error), Is.True, error);
            return node;
        }

        private enum Weather
        {
            Clear,
            Rain,
        }
    }
}
