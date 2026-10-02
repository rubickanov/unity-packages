using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class DiscoveryTests
    {
        [Test]
        public void ShouldScan_ConsoleAssembly_IsScanned()
        {
            Assert.IsTrue(CommandRegistry.ShouldScan(typeof(CommandRegistry).Assembly));
        }

        [Test]
        public void ShouldScan_AssemblyReferencingTheConsole_IsScanned()
        {
            Assert.IsTrue(CommandRegistry.ShouldScan(typeof(DiscoveryTests).Assembly));
        }

        [Test]
        public void ShouldScan_AssemblyNotReferencingTheConsole_IsSkipped()
        {
            Assert.IsFalse(CommandRegistry.ShouldScan(typeof(Assert).Assembly));
        }

        [Test]
        public void Initialize_CommandInAReferencingAssembly_IsDiscovered()
        {
            var registry = new CommandRegistry();

            registry.Initialize();

            Assert.IsTrue(registry.Commands.ContainsKey("discovery probe"));
            Assert.IsTrue(registry.Commands.ContainsKey("log unity"));
        }

        private static class StaticCommands
        {
            [ConsoleCommand("discovery probe", "Found by discovery in the test assembly", "Test")]
            public static string Probe() => "found";
        }
    }
}
