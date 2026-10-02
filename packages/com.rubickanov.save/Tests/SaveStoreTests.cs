using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Rubickanov.Save.Tests.TestAreas;
using Object = UnityEngine.Object;

namespace Rubickanov.Save.Tests
{
    /// <summary>What every save store does, the desktop's over files and the one in memory alike.</summary>
    [TestFixture(Kind.Desktop)]
    [TestFixture(Kind.Memory)]
    public sealed class SaveStoreTests
    {
        private readonly Kind _kind;
        private string _folder;
        private ISaveStore _store;

        public SaveStoreTests(Kind kind)
        {
            _kind = kind;
        }

        public enum Kind
        {
            Desktop,
            Memory,
        }

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "rubickanov-save-" + Guid.NewGuid().ToString("N"));
            _store = _kind == Kind.Desktop ? new DesktopSaveStore(_folder) : new MemorySaveStore();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void WriteAsync_Bytes_ReadBackWholeOrByStart()
        {
            var key = new SaveKey(Covers, "s01/course01.0123456789ab.jpg");
            byte[] bytes = Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray();

            Write(key, bytes);

            Assert.That(Read(key), Is.EqualTo(bytes));
            Assert.That(_store.ReadStartAsync(key, 10).GetAwaiter().GetResult(), Is.EqualTo(bytes.Take(10)));
            Assert.That(_store.ReadStartAsync(key, 10_000).GetAwaiter().GetResult(), Is.EqualTo(bytes));
            Assert.That(Find(key)?.Size, Is.EqualTo(bytes.Length));
        }

        [Test]
        public void ReadTextAsync_AnyByteOrderMark_ReadsTheText()
        {
            var key = new SaveKey(Settings, "settings.json");
            const string text = "{ \"name\": \"Гоша ✓\" }\n";

            _store.WriteTextAsync(key, text).GetAwaiter().GetResult();

            Assert.That(_store.ReadTextAsync(key).GetAwaiter().GetResult(), Is.EqualTo(text));
            Assert.That(Read(key)[0], Is.EqualTo((byte)'{'), "UTF-8 without a byte order mark");
            Write(key, Encoding.UTF8.GetPreamble().Concat(SaveText.Encode(text)).ToArray());
            Assert.That(_store.ReadTextAsync(key).GetAwaiter().GetResult(), Is.EqualTo(text));
            Write(key, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray());
            Assert.That(_store.ReadTextAsync(key).GetAwaiter().GetResult(), Is.EqualTo(text));
        }

        [Test]
        public void WriteAsync_OverLongerData_ReplacesTheWhole()
        {
            var key = new SaveKey(TestAreas.Progress, "progress.json");
            Write(key, new byte[1000]);
            DateTime first = Find(key)!.Value.Written;

            Write(key, new byte[] { 7, 8 });

            Assert.That(Read(key), Is.EqualTo(new byte[] { 7, 8 }));
            Assert.That(Find(key)!.Value.Written, Is.GreaterThanOrEqualTo(first));
            Assert.That(List(TestAreas.Progress).Select(entry => entry.Key.Name), Is.EqualTo(new[] { "progress.json" }),
                "nothing half-written left beside it");
        }

        [Test]
        public void Calls_MissingKey_FindNothing()
        {
            var key = new SaveKey(Replays, "none.sdreplay");

            Assert.That(Read(key), Is.Null);
            Assert.That(_store.ReadStartAsync(key, 10).GetAwaiter().GetResult(), Is.Null);
            Assert.That(Find(key), Is.Null);
            Assert.That(_store.DeleteAsync(key).GetAwaiter().GetResult(), Is.False);
            Assert.That(List(Replays), Is.Empty);
        }

        [Test]
        public void DeleteAsync_KeptKey_IsForgotten()
        {
            var key = new SaveKey(Controls, "controls.json");
            Write(key, new byte[] { 1 });

            Assert.That(_store.DeleteAsync(key).GetAwaiter().GetResult(), Is.True);

            Assert.That(Read(key), Is.Null);
            Assert.That(List(Controls), Is.Empty);
        }

        [Test]
        public void ListAsync_SeveralAreas_GivesOneAreasNamesWithThePrefixInOrder()
        {
            Write(new SaveKey(Covers, "s02/a.jpg"), new byte[] { 1 });
            Write(new SaveKey(Covers, "s01/box.jpg"), new byte[] { 1, 2 });
            Write(new SaveKey(Covers, "s01/a.jpg"), new byte[] { 1, 2, 3 });
            Write(new SaveKey(Seasons, "s01/season.json"), new byte[] { 1 });
            Write(new SaveKey(Settings, "settings.json"), new byte[] { 1 });
            Write(new SaveKey(TestAreas.Progress, "progress.json"), new byte[] { 1 });

            Assert.That(List(Covers, "s01/").Select(entry => entry.Key.Name), Is.EqualTo(new[] { "s01/a.jpg", "s01/box.jpg" }));
            Assert.That(List(Covers, "s01/").Select(entry => entry.Size), Is.EqualTo(new[] { 3L, 2L }));
            Assert.That(List(Covers).Select(entry => entry.Key.Name), Is.EqualTo(new[] { "s01/a.jpg", "s01/box.jpg", "s02/a.jpg" }));
            Assert.That(List(Covers, "s01/b").Select(entry => entry.Key.Name), Is.EqualTo(new[] { "s01/box.jpg" }));
            Assert.That(List(Settings).Select(entry => entry.Key.Name), Is.EqualTo(new[] { "settings.json" }),
                "the settings beside the progress see only their own");
            Assert.That(List(Seasons).Single().Key, Is.EqualTo(new SaveKey(Seasons, "s01/season.json")));
        }

        [UnityTest]
        [Timeout(20000)]
        public IEnumerator ReadPictureAsync_JpegOrBroken_DecodesMippedAndClampedOrGivesNull() => UniTask.ToCoroutine(async () =>
        {
            var cover = new SaveKey(Covers, "s01/cover.jpg");
            var broken = new SaveKey(Covers, "s01/broken.jpg");
            Write(cover, Jpeg(64, 32));
            Write(broken, new byte[] { 1, 2, 3 });

            Texture2D texture = await _store.ReadPictureAsync(cover);
            try
            {
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.width, Is.EqualTo(64));
                Assert.That(texture.height, Is.EqualTo(32));
                Assert.That(texture.mipmapCount, Is.GreaterThan(1));
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(texture.isReadable, Is.False, "never held in memory twice");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            Assert.That(await _store.ReadPictureAsync(broken), Is.Null);
            Assert.That(await _store.ReadPictureAsync(new SaveKey(Covers, "s01/missing.jpg")), Is.Null);
        });

        private static byte[] Jpeg(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                return texture.EncodeToJPG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private void Write(SaveKey key, byte[] bytes) => _store.WriteAsync(key, bytes).GetAwaiter().GetResult();

        private byte[] Read(SaveKey key) => _store.ReadAsync(key).GetAwaiter().GetResult();

        private SaveEntry? Find(SaveKey key) => _store.FindAsync(key).GetAwaiter().GetResult();

        private SaveEntry[] List(SaveArea area, string prefix = "") =>
            _store.ListAsync(area, prefix).GetAwaiter().GetResult().ToArray();
    }
}
