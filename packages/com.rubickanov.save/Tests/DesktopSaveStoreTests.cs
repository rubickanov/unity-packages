using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using static Rubickanov.Save.Tests.TestAreas;

namespace Rubickanov.Save.Tests
{
    /// <summary>
    /// The desktop store lays out each area as it says, so saves a game made before the store read on as they were, and
    /// a write replaces its file whole or not at all.
    /// </summary>
    public sealed class DesktopSaveStoreTests
    {
        private string _folder;
        private DesktopSaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "rubickanov-desktop-" + Guid.NewGuid().ToString("N"));
            _store = new DesktopSaveStore(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [TestCase("Settings", "settings.json", "settings.json")]
        [TestCase("Settings", "settings.broken.json", "settings.broken.json")]
        [TestCase("Progress", "progress.json", "progress.json")]
        [TestCase("Controls", "controls.json", "controls.json")]
        [TestCase("Replays", "s01-course01_20260930-214501.sdreplay", "Replays/s01-course01_20260930-214501.sdreplay")]
        [TestCase("Seasons", "mine/season.json", "Seasons/mine/season.json")]
        [TestCase("Seasons", "mine/art/box.png", "Seasons/mine/art/box.png")]
        [TestCase("Covers", "s01/course01.0123456789ab.jpg", "Covers/s01/course01.0123456789ab.jpg")]
        [TestCase("Covers", "s01/box.0123456789ab.jpg", "Covers/s01/box.0123456789ab.jpg")]
        public void Calls_AreaAtTheRootOrInAFolder_UseTheFileItsLayoutNames(string areaName, string name, string file)
        {
            SaveArea area = AreaNamed(areaName);
            string path = Path.Combine(_folder, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var key = new SaveKey(area, name);

            Assert.That(_store.ReadAsync(key).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 1, 2, 3 }),
                "a file saved before reads on");
            Assert.That(_store.Describe(key), Is.EqualTo(path));
            _store.WriteAsync(key, new byte[] { 4 }).GetAwaiter().GetResult();
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 4 }));
            Assert.That(_store.ListAsync(area).GetAwaiter().GetResult().Select(entry => entry.Key), Is.EqualTo(new[] { key }));
        }

        [Test]
        public void ListAsync_AreaAtTheRoot_SkipsOtherAreasAndFolders()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllBytes(Path.Combine(_folder, "settings.json"), new byte[1]);
            File.WriteAllBytes(Path.Combine(_folder, "progress.json"), new byte[1]);
            File.WriteAllBytes(Path.Combine(_folder, "settings"), new byte[1]);
            Directory.CreateDirectory(Path.Combine(_folder, "Covers"));
            File.WriteAllBytes(Path.Combine(_folder, "Covers", "settings.json"), new byte[1]);

            string[] names = _store.ListAsync(Settings).GetAwaiter().GetResult().Select(entry => entry.Key.Name).ToArray();

            Assert.That(names, Is.EqualTo(new[] { "settings.json" }));
        }

        [Test]
        public void WriteAsync_TempFileCannotBeMade_ThrowsAndLeavesWhatWasThere()
        {
            var key = new SaveKey(Progress, "progress.json");
            _store.WriteAsync(key, new byte[] { 1, 2, 3 }).GetAwaiter().GetResult();
            // The write's own file cannot be made: a folder stands where it would go.
            Directory.CreateDirectory(_store.Describe(key) + DesktopSaveStore.TempEnd);

            Assert.Throws<SaveStoreException>(() => _store.WriteAsync(key, new byte[] { 9 }).GetAwaiter().GetResult());

            Assert.That(_store.ReadAsync(key).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void WriteAsync_AfterAWriteCutShort_TakesThePlaceOfItsTempFileWhichListingsNeverSee()
        {
            var key = new SaveKey(Settings, "settings.json");
            _store.WriteAsync(key, new byte[] { 1 }).GetAwaiter().GetResult();
            string temp = _store.Describe(key) + DesktopSaveStore.TempEnd;
            File.WriteAllBytes(temp, new byte[] { 6, 6 });

            Assert.That(_store.ListAsync(Settings).GetAwaiter().GetResult().Select(entry => entry.Key.Name),
                Is.EqualTo(new[] { "settings.json" }));
            Assert.That(_store.ReadAsync(key).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 1 }));

            _store.WriteAsync(key, new byte[] { 2 }).GetAwaiter().GetResult();

            Assert.That(_store.ReadAsync(key).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 2 }));
            Assert.That(File.Exists(temp), Is.False);
        }

        private static SaveArea AreaNamed(string name) => name switch
        {
            "Settings" => Settings,
            "Progress" => Progress,
            "Controls" => Controls,
            "Replays" => Replays,
            "Seasons" => Seasons,
            _ => Covers,
        };
    }
}
