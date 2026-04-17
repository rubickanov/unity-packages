using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class AliasRegistryTests
    {
        private AliasRegistry _aliases = null!;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey("DevConsole_Aliases");
            _aliases = AliasRegistry.Instance;
            _aliases.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _aliases.Clear();
            PlayerPrefs.DeleteKey("DevConsole_Aliases");
        }

        [Test]
        public void TryResolve_KnownAlias_ReturnsCommand()
        {
            _aliases.Set("g", "give");

            var ok = _aliases.TryResolve("g", out var command);

            Assert.IsTrue(ok);
            Assert.AreEqual("give", command);
        }

        [Test]
        public void TryResolve_UnknownAlias_ReturnsFalse()
        {
            var ok = _aliases.TryResolve("missing", out var command);

            Assert.IsFalse(ok);
            Assert.IsNull(command);
        }

        [Test]
        public void Set_ThenRemove_TryResolveReturnsFalse()
        {
            _aliases.Set("g", "give");
            _aliases.Remove("g");

            Assert.IsFalse(_aliases.TryResolve("g", out _));
        }

        [Test]
        public void Clear_RemovesAllAliasesAndPersistedKey()
        {
            _aliases.Set("a", "alpha");
            _aliases.Set("b", "beta");

            _aliases.Clear();

            Assert.IsFalse(_aliases.TryResolve("a", out _));
            Assert.IsFalse(_aliases.TryResolve("b", out _));
            Assert.IsFalse(PlayerPrefs.HasKey("DevConsole_Aliases"));
        }

        [Test]
        public void Execute_AliasRecursionLimit_BailsAfter8Levels()
        {
            var registry = new CommandRegistry();
            registry.Register("real", _ => "ok");
            for (int i = 0; i < 10; i++)
                _aliases.Set($"a{i}", $"a{i + 1}");

            var result = registry.Execute("a0");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("recursion limit", result.Message ?? "");
        }
    }
}
