using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public partial class DiscoveryTests
    {
        [Test]
        public void Initialize_CommandInAReferencingAssembly_IsRegistered()
        {
            var registry = new CommandRegistry();

            registry.Initialize();

            Assert.IsTrue(registry.Commands.ContainsKey("discovery probe"));
            Assert.AreEqual("found", registry.Execute("discovery probe").Message);
        }

        [Test]
        public void Initialize_ThePackagesOwnAttributeCommands_AreRegistered()
        {
            var registry = new CommandRegistry();

            registry.Initialize();

            Assert.IsTrue(registry.Commands.ContainsKey("log unity"));
            Assert.IsTrue(registry.Commands.ContainsKey("fps target"));
        }

        [Test]
        public void Initialize_CommandInAPrivateNestedType_IsRegisteredThroughItsPartialDeclaration()
        {
            var registry = new CommandRegistry();

            registry.Initialize();

            Assert.AreEqual("hidden", registry.Execute("discovery hidden").Message);
        }

        // Internal all the way out: the assembly's generated class registers it
        internal static class StaticCommands
        {
            [ConsoleCommand("discovery probe", "Found by discovery in the test assembly", "Test")]
            public static string Probe() => "found";
        }

        // Private: the code is generated inside, so the fixture and the class are partial
        private static partial class HiddenCommands
        {
            [ConsoleCommand("discovery hidden", "Registered from inside its own type", "Test")]
            private static string Hidden() => "hidden";
        }
    }
}
