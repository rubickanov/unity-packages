using NUnit.Framework;

namespace Rubickanov.GameplayTags.Tests
{
    [TestFixture]
    public class SerializedGameplayTagTests
    {
        [SetUp]
        public void SetUp()
        {
            TagTestFixtures.InstallStandardRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            TagTestFixtures.EnsureUninstalled();
        }

        [Test]
        public void Constructor_ValidPath_PathExposed()
        {
            var serialized = new SerializedGameplayTag("Damage.Fire");

            Assert.AreEqual("Damage.Fire", serialized.Path);
        }

        [Test]
        public void Constructor_NullPath_PathIsEmptyString()
        {
            var serialized = new SerializedGameplayTag(null!);

            Assert.AreEqual("", serialized.Path);
        }

        [Test]
        public void Tag_ValidPath_ResolvesToRegisteredTag()
        {
            var serialized = new SerializedGameplayTag("Damage.Fire");
            var expected = TagTestFixtures.Tag("Damage.Fire");

            Assert.AreEqual(expected, serialized.Tag);
        }

        [Test]
        public void Tag_UnknownPath_ReturnsNone()
        {
            var serialized = new SerializedGameplayTag("Does.Not.Exist");

            Assert.AreEqual(GameplayTag.None, serialized.Tag);
        }

        [Test]
        public void Tag_EmptyPath_ReturnsNone()
        {
            var serialized = new SerializedGameplayTag("");

            Assert.AreEqual(GameplayTag.None, serialized.Tag);
        }

        [Test]
        public void Tag_RegistryNotInstalled_ReturnsNone()
        {
            var serialized = new SerializedGameplayTag("Damage.Fire");

            try
            {
                GameplayTagRegistry.Uninstall();

                Assert.AreEqual(GameplayTag.None, serialized.Tag);
            }
            finally
            {
                TagTestFixtures.InstallStandardRegistry();
            }
        }

        [Test]
        public void OnAfterDeserialize_MarksDirtyAndReResolves()
        {
            var serialized = new SerializedGameplayTag("Damage.Fire");
            // Prime the cache
            var first = serialized.Tag;
            Assert.IsTrue(first.IsValid);

            // Swap the registry to a registry that does NOT know "Damage.Fire".
            GameplayTagRegistry.Uninstall();
            GameplayTagRegistry.Install(new GameplayTagRegistry(new[] { "Unrelated" }));

            // Without re-marking dirty, cached tag may be stale. OnAfterDeserialize forces re-resolve.
            serialized.OnAfterDeserialize();

            Assert.AreEqual(GameplayTag.None, serialized.Tag);
        }

        [Test]
        public void Path_ReflectsConstructorArgument()
        {
            var serialized = new SerializedGameplayTag("Status.Stun");

            Assert.AreEqual("Status.Stun", serialized.Path);
        }
    }
}
