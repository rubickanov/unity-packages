using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Rubickanov.Save.Data;
using Rubickanov.Save.World;
using UnityEngine.TestTools;

namespace Rubickanov.Save.Tests.World
{
    public sealed class BackgroundLoadTests
    {
        private static readonly SaveArea Area = SaveArea.InFolder("Saves");

        [UnityTest]
        public IEnumerator Metadata_ListsAndFindsWithoutReadingPayloads() => UniTask.ToCoroutine(async () =>
        {
            var store = new FaultySaveStore(new MemorySaveStore());
            var slots = new SaveSlots(store, Area, "test");
            string slot = Seed(slots);
            store.Corrupt(new SaveKey(Area, slot + "/value.000002.json"));
            int main = Thread.CurrentThread.ManagedThreadId;
            store.OnRead = key =>
            {
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.Not.EqualTo(main));
                Assert.That(key.Leaf, Does.StartWith("manifest."));
            };
            store.OnList = _ => Assert.That(Thread.CurrentThread.ManagedThreadId, Is.Not.EqualTo(main));

            IReadOnlyList<SlotInfo> list = await slots.ListMetadataAsync();
            SlotInfo found = await slots.FindMetadataAsync(slot);

            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(main));
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(found.Generation, Is.EqualTo(2));
            Assert.That(found.IntegrityChecked, Is.False);
            Assert.That(found.Error, Is.Null, "payload integrity is intentionally unknown");
            store.OnRead = null;
            store.OnList = null;
            SlotInfo verified = await slots.FindAsync(slot, background: true);
            Assert.That(verified.IntegrityChecked, Is.True);
            Assert.That(verified.Generation, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator BackgroundLoad_DesktopReadsOnPoolChecksAndRestoresOnMain() => UniTask.ToCoroutine(async () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "save-background-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FaultySaveStore(new DesktopSaveStore(folder));
                var slots = new SaveSlots(store, Area, "test");
                string slot = Seed(slots);
                int main = Thread.CurrentThread.ManagedThreadId;
                int reads = 0;
                store.OnRead = _ =>
                {
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.Not.EqualTo(main));
                    Interlocked.Increment(ref reads);
                };
                store.OnList = _ => Assert.That(Thread.CurrentThread.ManagedThreadId, Is.Not.EqualTo(main));
                var section = new CheckedSection(main);
                using SaveWorld world = slots.Open(new[] { section });

                SaveReport report = await world.LoadInBackgroundAsync(slot);

                Assert.That(report.Ok, Is.True, report.ToString());
                Assert.That(reads, Is.EqualTo(3), "one manifest, one section, one attachment");
                Assert.That(section.Value, Is.EqualTo(7));
                Assert.That(section.Checked, Is.True);
                Assert.That(section.Migrated, Is.True);
                Assert.That(section.Blob, Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(main));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        });

        [UnityTest]
        public IEnumerator BackgroundLoad_BrokenAttachmentFallsBackBeforeAnyRestore() => UniTask.ToCoroutine(async () =>
        {
            var errors = new List<string>();
            var store = new FaultySaveStore(new MemorySaveStore());
            var slots = new SaveSlots(store, Area, "test", errors.Add);
            string slot = Seed(slots);
            store.Corrupt(new SaveKey(Area, slot + "/value.blob.000002.bin"));
            int restored = 0;
            using SaveWorld world = slots.Open(new[] { SaveSection.Of("value", null, _ => { }, _ => restored++) });

            SaveReport report = await world.LoadInBackgroundAsync(slot);

            Assert.That(report.Ok, Is.True);
            Assert.That(report.Generation, Is.EqualTo(1));
            Assert.That(report.FellBackFrom, Is.EqualTo(2));
            Assert.That(restored, Is.Zero);
            Assert.That(await store.FindAsync(new SaveKey(Area, slot + "/value.blob.000002.broken.bin")), Is.Not.Null);
        });

        [UnityTest]
        public IEnumerator BackgroundLoad_CancelDuringReadNeverRestoresAndReleasesSlot() =>
            UniTask.ToCoroutine(() => InterruptedLoad(false));

        [UnityTest]
        public IEnumerator BackgroundLoad_CloseDuringReadNeverRestoresAndReleasesSlot() =>
            UniTask.ToCoroutine(() => InterruptedLoad(true));

        [UnityTest]
        public IEnumerator BackgroundLoad_CheckRejectsGenerationBeforeRestore() => UniTask.ToCoroutine(async () =>
        {
            var store = new FaultySaveStore(new MemorySaveStore());
            var slots = new SaveSlots(store, Area, "test", _ => { });
            string slot = Seed(slots);
            var section = new CheckedSection(Thread.CurrentThread.ManagedThreadId) { Reject = true };
            using SaveWorld world = slots.Open(new[] { section });

            SaveReport report = await world.LoadInBackgroundAsync(slot);

            Assert.That(report.Ok, Is.True);
            Assert.That(report.Generation, Is.EqualTo(1));
            Assert.That(report.FellBackFrom, Is.EqualTo(2));
            Assert.That(section.Checked, Is.True);
            Assert.That(section.Value, Is.Zero);
        });

        private static async UniTask InterruptedLoad(bool close)
        {
            var store = new FaultySaveStore(new MemorySaveStore());
            var slots = new SaveSlots(store, Area, "test");
            string slot = Seed(slots);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var cancel = new CancellationTokenSource();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            store.OnRead = _ =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The test did not release the read.");
                }
            };
            int restored = 0;
            using SaveWorld world = slots.Open(new[] { SaveSection.Of("value", null, _ => { }, _ => restored++) });
            UniTask<bool> loading = world.LoadInBackgroundAsync(slot, cancel.Token).SuppressCancellationThrow()
                .ContinueWith(result => result.IsCanceled);
            try
            {
                await UniTask.WaitUntil(() => entered.IsSet, cancellationToken: timeout.Token);
                if (close)
                {
                    world.Close();
                }
                else
                {
                    cancel.Cancel();
                }
            }
            finally
            {
                release.Set();
            }

            Assert.That(await loading, Is.True);
            Assert.That(restored, Is.Zero);
            Assert.That(world.Loaded, Is.False);
            store.OnRead = null;
            using SaveWorld again = slots.Open(Array.Empty<ISaveSection>());
            Assert.That((await again.LoadInBackgroundAsync(slot)).Ok, Is.True);
        }

        private static string Seed(SaveSlots slots)
        {
            string slot = slots.CreateAsync("Beach").GetAwaiter().GetResult().Id;
            using SaveWorld world = slots.Open(new[]
            {
                SaveSection.Of("value", null, w =>
                {
                    w.Int("n", 7);
                    w.Attach("blob", new byte[] { 1, 2, 3 });
                }, _ => { }),
            });
            Assert.That(world.LoadAsync(slot).GetAwaiter().GetResult().Ok, Is.True);
            world.SaveNow();
            return slot;
        }

        private sealed class CheckedSection : ISaveSection, ISaveSectionCheck
        {
            private readonly int _main;

            public CheckedSection(int main)
            {
                _main = main;
                Migrations = new SaveMigrations().Step(2, _ =>
                {
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(_main));
                    Migrated = true;
                });
            }

            public string Id => "value";
            public SaveMigrations Migrations { get; }
            public int Value;
            public byte[] Blob;
            public bool Checked;
            public bool Migrated;
            public bool Reject;

            public void Capture(SaveWriter w) { }

            public void Restore(SaveReader r)
            {
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(_main));
                Value = r.Int("n");
                Blob = r.Attachment("blob");
            }

            public SaveCheck Check(SaveNode data)
            {
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(_main));
                Checked = true;
                return Reject ? SaveCheck.Broken("rejected by the game") : SaveCheck.Ok;
            }
        }
    }
}
