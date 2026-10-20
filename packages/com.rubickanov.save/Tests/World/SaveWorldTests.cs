using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Rubickanov.Save.Data;
using Rubickanov.Save.World;

namespace Rubickanov.Save.Tests.World
{
    public sealed class SaveWorldTests
    {
        private static readonly SaveArea Saves = SaveArea.InFolder("Saves");

        private static string SlotFixtures => Path.GetFullPath("Packages/com.rubickanov.save/Tests/Fixtures/slot");

        private MemorySaveStore _memory;
        private FaultySaveStore _store;
        private List<string> _errors;

        [SetUp]
        public void MakeStore()
        {
            _memory = new MemorySaveStore();
            _store = new FaultySaveStore(_memory);
            _errors = new List<string>();
        }

        [Test]
        public void Check_SlotFixtures_EveryVersionReadsAsExpected()
        {
            Assert.That(SaveFixtures.Check(SlotFixtures, SaveSlots.ManifestFormat), Is.Empty);
        }

        [Test]
        public void SaveAsync_StoreDiesAtEveryWrite_SlotLoadsTheOldOrTheNewGenerationWhole()
        {
            for (int k = 1; k <= 4; k++)
            {
                MakeStore();
                Game game = Started(out string slot);
                game.A = game.B = 1;
                Assert.That(Saved(game), Is.True);
                game.A = game.B = 2;
                _store.FailFrom(k);

                bool ok = Saved(game);

                _store.Heal();
                Game after = Loaded(slot, out SaveReport report);
                Assert.That(report.Ok, Is.True, $"k={k}: {report}");
                Assert.That(report.FellBackFrom, Is.Null, $"k={k}: a dead write leaves no broken generation");
                Assert.That((after.A, after.B), Is.EqualTo(ok ? (2, 2) : (1, 1)), $"k={k}, gen {report.Generation}");
                Assert.That(report.Generation, Is.EqualTo(ok ? 3 : 2), $"k={k}");
                Assert.That(ok, Is.EqualTo(k > 3), $"k={k}: a, b and the manifest are three writes");
            }
        }

        [TestCase("manifest.000003.json", "manifest.000003.json is broken")]
        [TestCase("a.000003.json", "a.000003.json is broken (its length or CRC is not the manifest's)")]
        public void LoadAsync_NewestGenerationBroken_FallsBackToThePreviousAndSaysSo(string file, string message)
        {
            Game game = Started(out string slot);
            game.A = 1;
            Saved(game);
            game.A = 2;
            Saved(game);
            _store.Corrupt(Key(slot, file));

            Game after = Loaded(slot, out SaveReport report);

            Assert.That(report.Generation, Is.EqualTo(2));
            Assert.That(report.FellBackFrom, Is.EqualTo(3));
            Assert.That(report.Notes.Any(n => n.Contains(message) && n.Contains("generation 2 used")), Is.True, string.Join("\n", report.Notes));
            Assert.That(_errors.Any(e => e.Contains(message)), Is.True, "the fallback is told");
            Assert.That(after.A, Is.EqualTo(1));
            Assert.That(Exists(slot, SlotFilesBroken(file)), Is.True, "the broken file is kept aside");
        }

        [Test]
        public void SaveAsync_AfterFallingBackFromABrokenSection_TwoSavesGiveAWholeGenerationWithoutTheBrokenFile()
        {
            Game game = Started(out string slot);
            game.A = 1;
            Saved(game);
            game.A = 2;
            Saved(game);
            _store.Corrupt(Key(slot, "a.000003.json"));
            Game middle = Loaded(slot, out _);

            middle.B = 7;
            Saved(middle);
            Saved(middle);
            middle.World.Close();
            Game after = Loaded(slot, out SaveReport report);

            Assert.That(report.Ok && report.FellBackFrom == null, Is.True, report.ToString());
            Assert.That(report.Generation, Is.EqualTo(5), "the broken copy's number is never reused");
            Assert.That((after.A, after.B), Is.EqualTo((1, 7)));
            Assert.That(Manifest(slot, 5).At("sections/a/file").String, Is.Not.EqualTo("a.000003.json"));
            Assert.That(Exists(slot, "a.000003.json"), Is.False, "swept");
            Assert.That(Exists(slot, "a.000003.broken.json"), Is.True, "never swept");
        }

        [Test]
        public void SaveAsync_AfterABrokenManifest_WritesTheNextGenerationAndDropsTheBrokenOne()
        {
            Game game = Started(out string slot);
            Saved(game);
            Saved(game);
            _store.Corrupt(Key(slot, "manifest.000003.json"));
            Game after = Loaded(slot, out _);

            Saved(after);

            Assert.That(Manifests(slot), Is.EqualTo(new[] { 1, 2, 4 }), "three whole manifests, the broken one gone");
            Assert.That(Exists(slot, "manifest.000003.broken.json"), Is.True);
        }

        [Test]
        public void LoadAsync_ACheckFindsTheNewestBroken_EverySectionComesFromThePreviousGeneration()
        {
            Game game = Started(out string slot);
            game.A = game.B = 1;
            Saved(game);
            game.A = 2;
            game.B = 13; // the check's poison
            Saved(game);

            Game after = Loaded(slot, out SaveReport report);

            Assert.That(report.Generation, Is.EqualTo(2));
            Assert.That((after.A, after.B), Is.EqualTo((1, 1)));
            Assert.That(report.Notes.Any(n => n.Contains("b.000003.json is broken (13 is cursed)")), Is.True, string.Join("\n", report.Notes));
        }

        [Test]
        public void Refused_ASectionNewerThanTheBuild_NothingWritesTheSlot()
        {
            Game newer = Started(out string slot, aVersion: 2);
            newer.A = 5;
            Saved(newer);
            newer.World.Close();
            string before = Digest();

            Game older = Loaded(slot, out SaveReport report);
            older.World.Save();
            older.World.SaveNow();
            bool saved = Pump(older.World, older.World.SaveAsync());
            bool renamed = Wait(older.Slots.RenameAsync(slot, "other"));
            older.World.Close();
            bool deleted = Wait(older.Slots.DeleteAsync(slot));

            Assert.That(report.Refused, Does.Contain("a v2; this build knows v1"));
            Assert.That(older.A, Is.EqualTo(0), "nothing restored");
            Assert.That((saved, renamed, deleted), Is.EqualTo((false, false, false)));
            Assert.That(Digest(), Is.EqualTo(before), "not a byte changed");
            Assert.That(Wait(older.Slots.FindAsync(slot)).Refused, Does.Contain("a v2"));
        }

        [Test]
        public void SaveAsync_ASectionThisBuildDoesNotKnow_IsCarriedAndComesBack()
        {
            Game full = Started(out string slot);
            full.Radio = 42;
            Saved(full);
            full.World.Close();

            Game without = Loaded(slot, out SaveReport report, radio: false);
            for (int i = 0; i < 3; i++)
            {
                without.A = i;
                Saved(without);
            }

            without.World.Close();
            Game back = Loaded(slot, out _);

            Assert.That(report.Notes.Any(n => n.Contains("section radio (v1) is unknown")), Is.True);
            Assert.That(Manifest(slot, 5).At("sections/radio/file").String, Is.EqualTo("radio.000002.json"));
            Assert.That(back.Radio, Is.EqualTo(42));
        }

        [Test]
        public void Save_ThreeWhileOneWrites_TwoGenerationsAndTheNewestCaptureLands()
        {
            Game game = Started(out string slot);
            _store.DelayMs = 100;

            game.A = 1;
            game.World.Save();
            game.A = 2;
            game.World.Save();
            game.A = 3;
            bool ok = Pump(game.World, game.World.SaveAsync());

            Assert.That(ok, Is.True);
            Assert.That(Manifests(slot), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(Value(slot, 2, "a"), Is.EqualTo(1));
            Assert.That(Value(slot, 3, "a"), Is.EqualTo(3));
        }

        [Test]
        public void SaveNow_WhileAWriteSleeps_WaitsForItAndWritesItsOwn()
        {
            Game game = Started(out string slot);
            _store.DelayMs = 200;
            game.A = 1;
            game.World.Save();
            game.A = 2;
            var watch = Stopwatch.StartNew();

            game.World.SaveNow();

            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(4000));
            Assert.That(Manifests(slot), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(Value(slot, 3, "a"), Is.EqualTo(2));
        }

        [Test]
        public void SaveAsync_ManySaves_KeepsThreeManifestsAndEveryFileTheyName()
        {
            Game game = Started(out string slot);
            for (int i = 0; i < 6; i++)
            {
                game.A = i;
                game.B = i / 3;
                Saved(game);
            }

            Assert.That(Manifests(slot), Is.EqualTo(new[] { 5, 6, 7 }));
            var named = new HashSet<string>();
            foreach (int g in Manifests(slot))
            {
                SaveNode sections = Manifest(slot, g)["sections"];
                foreach (string id in sections.Keys)
                {
                    named.Add(sections[id]["file"].String);
                }
            }

            var files = Files(slot).Where(f => !f.StartsWith("manifest.")).ToArray();
            Assert.That(files, Is.EquivalentTo(named));
            Assert.That(named, Does.Contain("b.000005.json"), "b unchanged since gen 5 is referenced, not rewritten");
        }

        [Test]
        public void Attachment_SavedAndLoaded_SameBytesAndUnchangedOnesAreReferenced()
        {
            Game game = Started(out string slot);
            game.Blob = new byte[] { 1, 2, 3, 250 };
            Saved(game);
            Saved(game);
            game.World.Close();

            Game after = Loaded(slot, out _);

            Assert.That(after.Blob, Is.EqualTo(new byte[] { 1, 2, 3, 250 }));
            Assert.That(Manifest(slot, 3).At("sections/a/attachments/blob/file").String, Is.EqualTo("a.blob.000002.bin"));
        }

        [Test]
        public void Open_ASectionCalledManifest_Throws()
        {
            var slots = new SaveSlots(_store, Saves, "test", _errors.Add);

            Assert.Throws<ArgumentException>(() => slots.Open(new[] { SaveSection.Of("manifest", null, _ => { }, _ => { }) }));
            Assert.Throws<ArgumentException>(() => slots.Open(new[] { SaveSection.Of("Clock", null, _ => { }, _ => { }) }));
        }

        private Game Started(out string slot, int aVersion = 1)
        {
            var game = new Game(_store, _errors.Add, aVersion, true);
            slot = Wait(game.Slots.CreateAsync("Beach")).Id;
            SaveReport report = Wait(game.World.LoadAsync(slot));
            Assert.That(report.Ok, Is.True, report.ToString());
            return game;
        }

        private Game Loaded(string slot, out SaveReport report, bool radio = true)
        {
            var game = new Game(_store, _errors.Add, 1, radio);
            report = Wait(game.World.LoadAsync(slot));
            return game;
        }

        private static bool Saved(Game game) => Pump(game.World, game.World.SaveAsync());

        private static T Wait<T>(UniTask<T> task) => task.AsTask().GetAwaiter().GetResult();

        private static bool Pump(SaveWorld world, UniTask<bool> task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.Status.IsCompleted())
            {
                world.Tick();
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(10000), "the save never finished");
                System.Threading.Thread.Sleep(1);
            }

            return task.GetAwaiter().GetResult();
        }

        private static SaveKey Key(string slot, string file) => new(Saves, slot + "/" + file);

        private static string SlotFilesBroken(string file) => file.Substring(0, file.Length - 5) + ".broken.json";

        private bool Exists(string slot, string file) => Wait(_memory.FindAsync(Key(slot, file))) != null;

        private string[] Files(string slot) =>
            Wait(_memory.ListAsync(Saves, slot + "/")).Select(e => e.Key.Name.Substring(slot.Length + 1)).ToArray();

        private int[] Manifests(string slot) =>
            Files(slot).Where(f => f.StartsWith("manifest.") && !f.Contains(".broken.")).Select(f => int.Parse(f.Split('.')[1])).OrderBy(g => g).ToArray();

        private SaveNode Manifest(string slot, int generation)
        {
            string text = Encoding.UTF8.GetString(Wait(_memory.ReadAsync(Key(slot, $"manifest.{generation:D6}.json"))));
            Assert.That(SaveJson.TryRead(text, out SaveNode root, out string error), Is.True, error);
            return root;
        }

        private int Value(string slot, int generation, string section)
        {
            string file = Manifest(slot, generation).At($"sections/{section}/file").String;
            SaveJson.TryRead(Encoding.UTF8.GetString(Wait(_memory.ReadAsync(Key(slot, file)))), out SaveNode root, out _);
            return (int)root["v"].Long;
        }

        private string Digest()
        {
            var text = new StringBuilder();
            using var sha = SHA256.Create();
            foreach (SaveEntry entry in Wait(_memory.ListAsync(Saves)))
            {
                text.Append(entry.Key.Name).Append(' ')
                    .Append(Convert.ToBase64String(sha.ComputeHash(Wait(_memory.ReadAsync(entry.Key))))).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>A game with three sections: a (versioned), b (checked: 13 is cursed) and radio (dropped by a build).</summary>
        private sealed class Game
        {
            public int A;
            public int B;
            public int Radio;
            public byte[] Blob;

            public Game(ISaveStore store, Action<string> logError, int aVersion, bool radio)
            {
                Slots = new SaveSlots(store, Saves, "test", logError);
                SaveMigrations aMigrations = aVersion > 1 ? new SaveMigrations().Step(2, _ => { }) : null;
                var order = new List<ISaveSection>
                {
                    SaveSection.Of("a", aMigrations, w =>
                    {
                        w.Int("v", A);
                        if (Blob != null)
                        {
                            w.Attach("blob", Blob);
                        }
                    }, r =>
                    {
                        A = r.Int("v");
                        Blob = r.Attachment("blob");
                    }),
                    new Checked(this),
                };
                if (radio)
                {
                    order.Add(SaveSection.Of("radio", null, w => w.Int("v", Radio), r => Radio = r.Int("v")));
                }

                World = Slots.Open(order);
            }

            public SaveSlots Slots { get; }

            public SaveWorld World { get; }

            private sealed class Checked : ISaveSection, ISaveSectionCheck
            {
                private readonly Game _game;

                public Checked(Game game) => _game = game;

                public string Id => "b";

                public SaveMigrations Migrations => null;

                public void Capture(SaveWriter w) => w.Int("v", _game.B);

                public void Restore(SaveReader r) => _game.B = r.Int("v");

                public SaveCheck Check(SaveNode data) => data["v"]?.Long == 13 ? SaveCheck.Broken("13 is cursed") : SaveCheck.Ok;
            }
        }
    }
}
