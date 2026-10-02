using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Rubickanov.Save.Tests.TestAreas;

namespace Rubickanov.Save.Tests
{
    /// <summary>
    /// A small document kept whole: read first, written in turn with the owner's text as it is when its turn comes,
    /// broken text kept aside, the store's failures logged.
    /// </summary>
    public sealed class SaveDocumentTests
    {
        private static readonly SaveKey Key = new(TestAreas.Progress, "progress.json");

        [Test]
        public void WriteAsync_BeforeTheReadIsDone_WaitsAndTakesTheTextWhenItsTurnComes()
        {
            var store = new SlowStore();
            store.Inner.WriteTextAsync(Key, "kept").GetAwaiter().GetResult();
            var document = new SaveDocument(store, Key);
            string state = "early";
            string read = null;

            UniTask reading = document.ReadAsync(text => read = text);
            UniTask<string> write = document.WriteAsync(() => state);
            state = "late";

            Assert.That(reading.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(store.Inner.ReadTextAsync(Key).GetAwaiter().GetResult(), Is.EqualTo("kept"), "nothing before the read");

            store.Release();

            Assert.That(read, Is.EqualTo("kept"));
            Assert.That(write.GetAwaiter().GetResult(), Is.EqualTo("late"));
            Assert.That(store.Inner.ReadTextAsync(Key).GetAwaiter().GetResult(), Is.EqualTo("late"));
        }

        [Test]
        public void WriteAsync_NullText_WritesNothing()
        {
            var store = new MemorySaveStore();
            var document = new SaveDocument(store, Key);

            Assert.That(document.WriteAsync(() => null).GetAwaiter().GetResult(), Is.Null);
            Assert.That(store.FindAsync(Key).GetAwaiter().GetResult(), Is.Null);
        }

        [Test]
        public void KeepBrokenAsync_Text_IsKeptBesideTheDocument()
        {
            var store = new MemorySaveStore();
            var document = new SaveDocument(store, Key);

            document.KeepBrokenAsync("{ broken").GetAwaiter().GetResult();

            Assert.That(document.BrokenKey, Is.EqualTo(new SaveKey(TestAreas.Progress, "progress.broken.json")));
            Assert.That(store.ReadTextAsync(document.BrokenKey).GetAwaiter().GetResult(), Is.EqualTo("{ broken"));
        }

        [Test]
        public void ReadAndWrite_StoreFails_ReportedToTheOwnerNotThrown()
        {
            var errors = new List<string>();
            var document = new SaveDocument(new FailingStore(), Key, errors.Add);
            string read = "not called";

            document.ReadAsync(text => read = text).GetAwaiter().GetResult();
            string written = document.WriteAsync(() => "x").GetAwaiter().GetResult();

            Assert.That(read, Is.Null);
            Assert.That(written, Is.Null);
            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(errors[0], Does.Match("could not be read: disk gone"));
            Assert.That(errors[1], Does.Match("could not be written: disk gone"));
        }

        [Test]
        public void ReadAsync_StoreFailsAndNoOwnerLog_GoesToUnityLog()
        {
            var document = new SaveDocument(new FailingStore(), Key);
            LogAssert.Expect(LogType.Error, new Regex("could not be read: disk gone"));

            document.ReadAsync(_ => { }).GetAwaiter().GetResult();
        }

        // A memory store whose reads wait for Release.
        private sealed class SlowStore : ISaveStore
        {
            private readonly List<UniTaskCompletionSource> _held = new();

            public MemorySaveStore Inner { get; } = new();

            public void Release()
            {
                foreach (UniTaskCompletionSource held in _held.ToArray())
                {
                    held.TrySetResult();
                }

                _held.Clear();
            }

            public async UniTask<byte[]> ReadAsync(SaveKey key, CancellationToken ct = default)
            {
                var held = new UniTaskCompletionSource();
                _held.Add(held);
                await held.Task;
                return await Inner.ReadAsync(key, ct);
            }

            public UniTask<byte[]> ReadStartAsync(SaveKey key, int count, CancellationToken ct = default) =>
                Inner.ReadStartAsync(key, count, ct);

            public UniTask WriteAsync(SaveKey key, byte[] bytes, CancellationToken ct = default) => Inner.WriteAsync(key, bytes, ct);

            public UniTask<bool> DeleteAsync(SaveKey key, CancellationToken ct = default) => Inner.DeleteAsync(key, ct);

            public UniTask<SaveEntry?> FindAsync(SaveKey key, CancellationToken ct = default) => Inner.FindAsync(key, ct);

            public UniTask<IReadOnlyList<SaveEntry>> ListAsync(SaveArea area, string prefix = "", CancellationToken ct = default) =>
                Inner.ListAsync(area, prefix, ct);

            public UniTask<Texture2D> ReadPictureAsync(SaveKey key, CancellationToken ct = default) =>
                Inner.ReadPictureAsync(key, ct);

            public string Describe(SaveKey key) => Inner.Describe(key);
        }

        private sealed class FailingStore : ISaveStore
        {
            public UniTask<byte[]> ReadAsync(SaveKey key, CancellationToken ct = default) => throw Gone();

            public UniTask<byte[]> ReadStartAsync(SaveKey key, int count, CancellationToken ct = default) => throw Gone();

            public UniTask WriteAsync(SaveKey key, byte[] bytes, CancellationToken ct = default) => throw Gone();

            public UniTask<bool> DeleteAsync(SaveKey key, CancellationToken ct = default) => throw Gone();

            public UniTask<SaveEntry?> FindAsync(SaveKey key, CancellationToken ct = default) => throw Gone();

            public UniTask<IReadOnlyList<SaveEntry>> ListAsync(SaveArea area, string prefix = "", CancellationToken ct = default) =>
                throw Gone();

            public UniTask<Texture2D> ReadPictureAsync(SaveKey key, CancellationToken ct = default) => throw Gone();

            public string Describe(SaveKey key) => key.ToString();

            private static SaveStoreException Gone() => new("disk gone", null);
        }
    }
}
