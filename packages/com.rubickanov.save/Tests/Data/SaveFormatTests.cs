using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.Tests.Data
{
    /// <summary>
    /// A document's versions: every released one walks the chain to the current one (golden files), a newer one is
    /// refused, broken text and a failing step are reported without touching what was read.
    /// </summary>
    public sealed class SaveFormatTests
    {
        // The till: whole dollars in v1, cents under a new name in v2, tips beside the cash in v3.
        private static readonly SaveFormat Till = new("till", new SaveMigrations()
            .Step(2, m => m.Rename("money", "cash").Edit("cash", n => SaveNode.Of(n.Long * 100)))
            .Step(3, m => m.Move("cash", "till/cash").Set("till/tips", SaveNode.Of(0))));

        private static string TillFixtures => Path.GetFullPath("Packages/com.rubickanov.save/Tests/Fixtures/till");

        private string _temp;

        [TearDown]
        public void DeleteTemp()
        {
            if (_temp != null && Directory.Exists(_temp))
            {
                Directory.Delete(_temp, true);
            }
        }

        [Test]
        public void Check_TillFixtures_EveryVersionReadsAsExpected()
        {
            Assert.That(SaveFixtures.Check(TillFixtures, Till), Is.Empty);
        }

        [Test]
        public void Check_NoExpectedFile_WritesItBesideAndReportsIt()
        {
            string folder = CopyFixtures("v1.json", "v1.expected.json", "v2.json", "v3.json");

            IReadOnlyList<string> problems = SaveFixtures.Check(folder, Till);

            Assert.That(problems, Is.EqualTo(new[] { "v2.expected.json: written, review the diff and commit" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "v2.expected.json")),
                Is.EqualTo(File.ReadAllText(Path.Combine(TillFixtures, "v2.expected.json"))));
        }

        [Test]
        public void Check_AReleasedVersionMissing_IsAProblem()
        {
            string folder = CopyFixtures("v2.json", "v2.expected.json", "v3.json");

            IReadOnlyList<string> problems = SaveFixtures.Check(folder, Till);

            Assert.That(problems, Is.EqualTo(new[] { "v1.json: missing; every released version stays as a file" }));
        }

        [Test]
        public void Read_ANewerVersion_IsRefusedWithoutData()
        {
            SaveRead read = Till.Read("{\"$kind\": \"till\", \"$version\": 4, \"$build\": \"0.9.0\", \"till\": {}}");

            Assert.That(read.Status, Is.EqualTo(SaveReadStatus.Newer));
            Assert.That(read.Data, Is.Null);
            Assert.That(read.Error, Is.EqualTo("made by 0.9.0 with till v4; this build knows v3"));
        }

        [Test]
        public void Read_BrokenText_IsBrokenAndSaysWhere()
        {
            SaveRead read = Till.Read("{\n  \"$kind\": \"till\",\n  \"cash\": tru\n}");

            Assert.That(read.Status, Is.EqualTo(SaveReadStatus.Broken));
            Assert.That(read.Error, Is.EqualTo("till: line 3, col 11: 't' where a value was expected"));
        }

        [Test]
        public void Walk_AStepThrows_ThrowsAndLeavesTheInputAlone()
        {
            var chain = new SaveMigrations()
                .Step(2, m => m.Remove("money").Edit("cash", _ => throw new InvalidOperationException("no cents")));
            var format = new SaveFormat("till", chain);
            SaveNode data = SaveNode.NewObject();
            data["money"] = SaveNode.Of(1);
            data["cash"] = SaveNode.Of(5);

            SaveRead read = format.Read(format.Write(data, "0.1.0").Replace("\"$version\": 2", "\"$version\": 1"));

            Assert.That(read.Status, Is.EqualTo(SaveReadStatus.Broken));
            Assert.That(read.Error, Is.EqualTo("till v1 could not be migrated to v2: no cents"));
            Assert.Throws<InvalidOperationException>(() => chain.Walk(data, 1));
            Assert.That(data["money"], Is.Not.Null, "the walk works on a copy");
        }

        [Test]
        public void Walk_OperationsOnMissingPaths_DoNothing()
        {
            var chain = new SaveMigrations().Step(2, m => m
                .Rename("money", "cash")
                .Move("old/cash", "till/cash")
                .Remove("nope/deep")
                .Edit("tips", _ => throw new InvalidOperationException("not called"))
                .Each("kept", e => e.Remove("mood")));
            SaveNode data = SaveNode.NewObject();
            data["cash"] = SaveNode.Of(7);

            SaveNode walked = chain.Walk(data, 1);

            Assert.That(SaveJson.Write(walked), Is.EqualTo("{\n  \"cash\": 7\n}\n"));
        }

        private string CopyFixtures(params string[] names)
        {
            _temp = Path.Combine(Path.GetTempPath(), "save-fixtures-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
            foreach (string name in names)
            {
                File.Copy(Path.Combine(TillFixtures, name), Path.Combine(_temp, name));
            }

            return _temp;
        }
    }
}
