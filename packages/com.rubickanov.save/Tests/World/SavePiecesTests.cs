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
    public sealed class SavePiecesTests
    {
        private const string First = "00000000000000000000000000000001";
        private const string Last = "ffffffffffffffffffffffffffffffff";

        private static readonly SaveArea Saves = SaveArea.InFolder("Saves");

        private static string KiteFixtures => Path.GetFullPath("Packages/com.rubickanov.save/Tests/Fixtures/pieces/kite");

        private MemorySaveStore _store;
        private List<string> _errors;

        [SetUp]
        public void MakeStore()
        {
            _store = new MemorySaveStore();
            _errors = new List<string>();
        }

        [Test]
        public void Load_ALinkToAPieceSpawnedAfterIt_ResolvesAndCapturesTheSameText()
        {
            Beach a = Started(out string slot);
            Thing rope = a.Make("rope", 1, First);
            Thing boat = a.Make("boat", 2.5, Last);
            rope.LinkTo(boat);
            a.Clock = 15;
            Assert.That(Saved(a), Is.True);
            int saved = Newest(slot);
            string text = SectionText(slot, saved, "pieces");

            Beach b = Loaded(slot, out SaveReport report);

            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(b.Spawned.Select(t => t.Kind), Is.EqualTo(new[] { "rope", "boat" }), "the rope spawns first");
            Thing ropeB = b.Spawned[0];
            Assert.That(ropeB.SaveId.ToString(), Is.EqualTo(First));
            Assert.That(ropeB.Linked, Is.SameAs(b.Pieces.Find(boat.SaveId)));
            Assert.That(ropeB.Linked.Value, Is.EqualTo(2.5));
            Assert.That(b.Clock, Is.EqualTo(15));

            Assert.That(Saved(b), Is.True);
            Assert.That(Manifest(slot, Newest(slot)).At("sections/pieces/file").String, Is.EqualTo($"pieces.{saved:D6}.json"),
                "capture → restore → capture gives the same text, so the old file is named");
            Assert.That(SectionText(slot, Newest(slot), "pieces"), Is.EqualTo(text));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void Save_AKindTheBuildDoesNotKnow_IsWrittenBackUnchangedAndLoadsInABuildThatKnowsIt()
        {
            Beach a = Started(out string slot);
            Thing kite = a.Make("kite", 4.5);
            a.Make("boat", 1);
            Assert.That(Saved(a), Is.True);
            string record = Record(slot, Newest(slot), kite.SaveId);

            Beach b = Loaded(slot, out SaveReport report, kite: 0);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(b.Pieces.Orphans, Is.EqualTo(1));
            Assert.That(b.Pieces.Count, Is.EqualTo(1));
            Assert.That(report.Notes.Any(n => n.Contains("\"kite\" is not known")), Is.True, string.Join("\n", report.Notes));
            for (int i = 0; i < 2; i++)
            {
                b.Clock = 7 + i;
                Assert.That(Saved(b), Is.True);
            }

            Assert.That(Record(slot, Newest(slot), kite.SaveId), Is.EqualTo(record));
            Assert.That(Manifest(slot, Newest(slot)).At("kinds/kite").Long, Is.EqualTo(1));

            Beach c = Loaded(slot, out report);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(c.Pieces.Orphans, Is.Zero);
            Assert.That(((Thing)c.Pieces.Find(kite.SaveId)).Value, Is.EqualTo(4.5));
            Assert.That(c.Clock, Is.EqualTo(8));
        }

        [Test]
        public void Restore_AFactoryReturningNull_KeepsTheRecordAndTheLinksToIt()
        {
            Beach a = Started(out string slot);
            Thing boat = a.Make("boat", 3);
            a.Make("rope", 1).LinkTo(boat);
            Assert.That(Saved(a), Is.True);
            string record = Record(slot, Newest(slot), boat.SaveId);

            Beach b = Loaded(slot, out SaveReport report, boats: false);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(b.Pieces.Orphans, Is.EqualTo(1));
            Thing rope = b.Spawned.Single();
            Assert.That(rope.Linked, Is.Null);
            Assert.That(report.Notes.Any(n => n.Contains("could not be spawned")), Is.True, string.Join("\n", report.Notes));
            Assert.That(report.Notes.Any(n => n.Contains($"links to {boat.SaveId}, which is not in the world")), Is.True,
                string.Join("\n", report.Notes));
            Assert.That(Saved(b), Is.True);
            Assert.That(Record(slot, Newest(slot), boat.SaveId), Is.EqualTo(record));

            Beach c = Loaded(slot, out report);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(c.Spawned.Single(t => t.Kind == "rope").Linked, Is.SameAs(c.Pieces.Find(boat.SaveId)));
        }

        [Test]
        public void Load_AKindAtAnOlderVersion_MigratesByItsFixturesAndChain()
        {
            Assert.That(PieceFixtures.Check(KiteFixtures, new PieceKind("kite", (_, _) => null, KiteMigrations(null))), Is.Empty);

            Beach a = Started(out string slot);
            Thing kite = a.Make("kite", 4.5);
            Assert.That(Saved(a), Is.True);

            Beach d = Loaded(slot, out SaveReport report, kite: 2);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(((Thing)d.Pieces.Find(kite.SaveId)).Value, Is.EqualTo(4.5), "len is length at v2");
            Assert.That(report.Notes.Any(n => n.Contains("kite migrated from v1 to v2")), Is.True, string.Join("\n", report.Notes));

            Assert.That(Saved(d), Is.True);
            Assert.That(Manifest(slot, Newest(slot)).At("kinds/kite").Long, Is.EqualTo(2));
            Assert.That(Record(slot, Newest(slot), kite.SaveId), Does.Contain("\"length\": 4.5").And.Not.Contain("\"len\""));
        }

        [Test]
        public void Load_AKindMigrationThatThrowsOnTheNewestGeneration_EverySectionComesFromThePreviousOne()
        {
            Beach a = Started(out string slot);
            Thing kite = a.Make("kite", 4.5);
            Thing boat = a.Make("boat", 1);
            a.Clock = 10;
            Assert.That(Saved(a), Is.True);
            int good = Newest(slot);
            kite.Value = 13;
            boat.Value = 2;
            a.Clock = 11;
            Assert.That(Saved(a), Is.True);

            Beach d = Loaded(slot, out SaveReport report, kite: 2, kiteStep: record =>
            {
                if (record["len"].Double == 13)
                {
                    throw new InvalidOperationException("13 is cursed");
                }
            });

            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(report.FellBackFrom, Is.EqualTo(good + 1));
            Assert.That(report.Generation, Is.EqualTo(good));
            Assert.That(report.Notes.Any(n => n.Contains("could not migrate: 13 is cursed")), Is.True, string.Join("\n", report.Notes));
            Assert.That(d.Clock, Is.EqualTo(10));
            Assert.That(((Thing)d.Pieces.Find(kite.SaveId)).Value, Is.EqualTo(4.5));
            Assert.That(((Thing)d.Pieces.Find(boat.SaveId)).Value, Is.EqualTo(1));
        }

        [Test]
        public void Refused_AKindNewerThanTheBuild_NothingWritesTheSlot()
        {
            Beach d = Started(out string slot, kite: 2);
            d.Make("kite", 4.5);
            Assert.That(Saved(d), Is.True);
            string before = Digest();

            Beach a = Loaded(slot, out SaveReport report);
            Assert.That(report.Refused, Does.Contain("kite v2; this build knows v1"));
            Assert.That(a.Spawned, Is.Empty);
            Assert.That(Saved(a), Is.False);
            Assert.That(Digest(), Is.EqualTo(before));

            // The records are checked too, should a manifest not list the kind.
            var pieces = new SavePieces(_errors.Add);
            pieces.Add(new PieceKind("kite", (_, _) => null));
            Assert.That(SaveJson.TryRead($"{{\"pieces\": {{\"{Last}\": {{\"$v\": 2, \"kind\": \"kite\"}}}}}}", out SaveNode data, out string error), Is.True, error);
            Assert.That(pieces.Check(data).Status, Is.EqualTo(SaveCheckStatus.Newer));
        }

        [Test]
        public void Refused_AKindNewerCarriedByABuildWithoutIt_StillRefusesABuildThatKnowsItOlder()
        {
            Beach a = Started(out string slot, kite: 2);
            a.Make("kite", 4.5);
            Assert.That(Saved(a), Is.True);

            Beach b = Loaded(slot, out SaveReport report, kite: 0);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(b.Pieces.Orphans, Is.EqualTo(1));
            b.Clock = 9;
            Assert.That(Saved(b), Is.True);
            Assert.That(Manifest(slot, Newest(slot)).At("kinds/kite").Long, Is.EqualTo(2), "an orphan's kind is listed too");

            Loaded(slot, out report, kite: 1);
            Assert.That(report.Refused, Does.Contain("kite v2; this build knows v1"));
        }

        private Beach Started(out string slot, int kite = 1)
        {
            var beach = new Beach(_store, _errors.Add, kite, true, null);
            slot = Wait(beach.Slots.CreateAsync("Beach")).Id;
            SaveReport report = Wait(beach.World.LoadAsync(slot));
            Assert.That(report.Ok, Is.True, report.ToString());
            return beach;
        }

        private Beach Loaded(string slot, out SaveReport report, int kite = 1, bool boats = true, Action<SaveNode> kiteStep = null)
        {
            var beach = new Beach(_store, _errors.Add, kite, boats, kiteStep);
            report = Wait(beach.World.LoadAsync(slot));
            return beach;
        }

        private static SaveMigrations KiteMigrations(Action<SaveNode> before) =>
            new SaveMigrations().Step(2, s =>
            {
                if (before != null)
                {
                    s.Run(before);
                }

                s.Rename("len", "length");
            });

        private static bool Saved(Beach beach)
        {
            UniTask<bool> task = beach.World.SaveAsync();
            var watch = Stopwatch.StartNew();
            while (!task.Status.IsCompleted())
            {
                beach.World.Tick();
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(10000), "the save never finished");
                System.Threading.Thread.Sleep(1);
            }

            return task.GetAwaiter().GetResult();
        }

        private static T Wait<T>(UniTask<T> task) => task.AsTask().GetAwaiter().GetResult();

        private static SaveKey Key(string slot, string file) => new(Saves, slot + "/" + file);

        private string Text(string slot, string file) => Encoding.UTF8.GetString(Wait(_store.ReadAsync(Key(slot, file))));

        private int Newest(string slot) =>
            Wait(_store.ListAsync(Saves, slot + "/manifest.")).Select(e => e.Key.Name.Split('.')).Where(p => p.Length == 3)
                .Max(p => int.Parse(p[1]));

        private SaveNode Manifest(string slot, int generation)
        {
            Assert.That(SaveJson.TryRead(Text(slot, $"manifest.{generation:D6}.json"), out SaveNode root, out string error), Is.True, error);
            return root;
        }

        private string SectionText(string slot, int generation, string section) =>
            Text(slot, Manifest(slot, generation).At($"sections/{section}/file").String);

        private string Record(string slot, int generation, SaveId id)
        {
            Assert.That(SaveJson.TryRead(SectionText(slot, generation, "pieces"), out SaveNode root, out string error), Is.True, error);
            return SaveJson.Write(root.At("pieces/" + id));
        }

        private string Digest()
        {
            var text = new StringBuilder();
            using var sha = SHA256.Create();
            foreach (SaveEntry entry in Wait(_store.ListAsync(Saves)))
            {
                text.Append(entry.Key.Name).Append(' ').Append(Convert.ToBase64String(sha.ComputeHash(Wait(_store.ReadAsync(entry.Key))))).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>A world of a clock and pieces: boats, ropes linked to them, and kites at v1 or v2 (or unknown at 0).</summary>
        private sealed class Beach
        {
            public readonly List<Thing> Spawned = new();
            public int Clock;

            public Beach(ISaveStore store, Action<string> logError, int kite, bool boats, Action<SaveNode> kiteStep)
            {
                KiteField = kite > 1 ? "length" : "len";
                Slots = new SaveSlots(store, Saves, "test", logError);
                Pieces = new SavePieces(logError);
                Pieces.Add(new PieceKind("boat", (id, r) => boats ? Spawn(id, "boat", r) : null));
                Pieces.Add(new PieceKind("rope", (id, r) => Spawn(id, "rope", r)));
                if (kite > 0)
                {
                    Pieces.Add(new PieceKind("kite", (id, r) => Spawn(id, "kite", r), kite > 1 ? KiteMigrations(kiteStep) : null));
                }

                World = Slots.Open(new ISaveSection[]
                {
                    SaveSection.Of("clock", null, w => w.Int("hour", Clock), r => Clock = r.Int("hour")),
                    Pieces,
                });
            }

            public string KiteField { get; }

            public SaveSlots Slots { get; }

            public SavePieces Pieces { get; }

            public SaveWorld World { get; }

            public Thing Make(string kind, double value, string id = null)
            {
                SaveId saveId = SaveId.New();
                Assert.That(id == null || SaveId.TryParse(id, out saveId), Is.True);
                var thing = new Thing(this, saveId, kind) { Value = value };
                Pieces.Track(thing);
                return thing;
            }

            private Thing Spawn(SaveId id, string kind, SaveReader r)
            {
                var thing = new Thing(this, id, kind);
                thing.Value = r.Double(thing.Field);
                if (SaveId.TryParse(r.String("link"), out SaveId link))
                {
                    thing.LinkId = link;
                }

                Spawned.Add(thing);
                return thing;
            }
        }

        private sealed class Thing : ISavePiece, ISavePieceLinks
        {
            private readonly Beach _beach;

            public Thing(Beach beach, SaveId id, string kind)
            {
                _beach = beach;
                SaveId = id;
                Kind = kind;
            }

            public SaveId SaveId { get; }

            public string Kind { get; }

            public double Value;

            // Kept as read even when it does not resolve, so a link to an orphan survives the save.
            public SaveId LinkId;

            public Thing Linked;

            public string Field => Kind == "kite" ? _beach.KiteField : "value";

            public void LinkTo(Thing other)
            {
                Linked = other;
                LinkId = other.SaveId;
            }

            public void Capture(SaveWriter w)
            {
                w.Double(Field, Value);
                if (!LinkId.IsEmpty)
                {
                    w.String("link", LinkId.ToString());
                }
            }

            public void Link(SaveReader r, SavePieces pieces) => Linked = pieces.Resolve<Thing>(r, "link");
        }
    }
}
