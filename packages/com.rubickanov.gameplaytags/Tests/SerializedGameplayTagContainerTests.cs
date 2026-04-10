using System.Reflection;
using NUnit.Framework;

namespace Rubickanov.GameplayTags.Tests
{
    [TestFixture]
    public class SerializedGameplayTagContainerTests
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

        private static SerializedGameplayTagContainer MakeContainer(params string[] paths)
        {
            var container = default(SerializedGameplayTagContainer);
            object boxed = container;
            typeof(SerializedGameplayTagContainer)
                .GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(boxed, paths);
            return (SerializedGameplayTagContainer)boxed;
        }

        [Test]
        public void Container_FreshStruct_IsEmpty()
        {
            var serialized = default(SerializedGameplayTagContainer);

            Assert.AreEqual(0, serialized.Container.Count);
            Assert.IsTrue(serialized.Container.IsEmpty);
        }

        [Test]
        public void Container_AfterDeserialize_ContainsAllResolvedTags()
        {
            var serialized = MakeContainer("Damage.Fire", "Status.Stun");
            serialized.OnAfterDeserialize();

            var container = serialized.Container;

            Assert.AreEqual(2, container.Count);
            Assert.IsTrue(container.HasTagExact(TagTestFixtures.Tag("Damage.Fire")));
            Assert.IsTrue(container.HasTagExact(TagTestFixtures.Tag("Status.Stun")));
        }

        [Test]
        public void Container_UnknownPaths_AreSkipped()
        {
            var serialized = MakeContainer("Damage.Fire", "Does.Not.Exist");
            serialized.OnAfterDeserialize();

            var container = serialized.Container;

            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.HasTagExact(TagTestFixtures.Tag("Damage.Fire")));
        }

        [Test]
        public void Container_NullOrEmptyPath_IsSkipped()
        {
            var serialized = MakeContainer("Damage.Fire", "", null!);
            serialized.OnAfterDeserialize();

            var container = serialized.Container;

            Assert.AreEqual(1, container.Count);
        }

        [Test]
        public void Container_RegistryNotInstalled_ReturnsEmpty()
        {
            var serialized = MakeContainer("Damage.Fire", "Status.Stun");

            try
            {
                GameplayTagRegistry.Uninstall();
                serialized.OnAfterDeserialize();

                Assert.AreEqual(0, serialized.Container.Count);
            }
            finally
            {
                TagTestFixtures.InstallStandardRegistry();
            }
        }

        [Test]
        public void Container_OnAfterDeserialize_RebuildsFromPaths()
        {
            var serialized = MakeContainer("Damage.Fire");
            var first = serialized.Container;
            Assert.AreEqual(1, first.Count);

            // Replace underlying paths via reflection on a boxed struct (direct SetValue would mutate a throwaway copy)
            object boxed = serialized;
            typeof(SerializedGameplayTagContainer)
                .GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(boxed, new[] { "Damage.Fire", "Status.Stun" });
            serialized = (SerializedGameplayTagContainer)boxed;
            serialized.OnAfterDeserialize();

            var rebuilt = serialized.Container;
            Assert.AreEqual(2, rebuilt.Count);
        }

        [Test]
        public void Paths_ReturnsOriginalList()
        {
            var serialized = MakeContainer("Damage.Fire", "Status.Stun");

            var paths = serialized.Paths;

            Assert.AreEqual(2, paths.Count);
            Assert.AreEqual("Damage.Fire", paths[0]);
            Assert.AreEqual("Status.Stun", paths[1]);
        }
    }
}
