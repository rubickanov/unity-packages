using System;
using NUnit.Framework;
using static Rubickanov.Save.Tests.TestAreas;

namespace Rubickanov.Save.Tests
{
    /// <summary>Names are relative and plain, and an area at the root takes only its own.</summary>
    public sealed class SaveKeyTests
    {
        [Test]
        public void Constructor_NameNotRelativeOrPlain_Throws()
        {
            foreach (string name in new[] { null, "", "/a", "a/", "a//b", "../a", "a/./b", "..", "a\\b", "C:/a" })
            {
                Assert.That(SaveKey.IsValidName(name), Is.False, name);
                Assert.Throws<ArgumentException>(() => _ = new SaveKey(Covers, name), name);
            }

            Assert.That(SaveKey.IsValidName("s01/art/box 2.png"), Is.True);
            Assert.That(SaveKey.IsValidName("a/.b/..c"), Is.True);
        }

        [Test]
        public void Constructor_AreaAtTheRoot_TakesOnlySingleNamesWithItsStem()
        {
            Assert.That(new SaveKey(Settings, "settings.broken.json").Name, Is.EqualTo("settings.broken.json"));

            Assert.Throws<ArgumentException>(() => _ = new SaveKey(Settings, "progress.json"));
            Assert.Throws<ArgumentException>(() => _ = new SaveKey(Settings, "settingsx.json"));
            Assert.Throws<ArgumentException>(() => _ = new SaveKey(Settings, "settings."));
            Assert.Throws<ArgumentException>(() => _ = new SaveKey(Progress, "Replays/x.sdreplay"));
        }

        [Test]
        public void Constructor_DefaultArea_Throws()
        {
            Assert.Throws<ArgumentException>(() => _ = new SaveKey(default, "settings.json"));
        }

        [Test]
        public void Declare_NameWithParts_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveArea.InFolder("Data/Replays"));
            Assert.Throws<ArgumentException>(() => SaveArea.InFolder(""));
            Assert.Throws<ArgumentException>(() => SaveArea.AtRoot("Settings", "a/b"));
        }

        [Test]
        public void Equals_SameAreaDeclaredTwice_IsEqual()
        {
            var key = new SaveKey(SaveArea.InFolder("Covers"), "s01/box.jpg");

            Assert.That(key, Is.EqualTo(new SaveKey(Covers, "s01/box.jpg")));
            Assert.That(key.GetHashCode(), Is.EqualTo(new SaveKey(Covers, "s01/box.jpg").GetHashCode()));
            Assert.That(key, Is.Not.EqualTo(new SaveKey(Seasons, "s01/box.jpg")));
            Assert.That(key.Leaf, Is.EqualTo("box.jpg"));
            Assert.That(key.ToString(), Is.EqualTo("Covers/s01/box.jpg"));
        }
    }
}
