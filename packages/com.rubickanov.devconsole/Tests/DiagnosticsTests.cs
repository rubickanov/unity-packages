using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class DiagnosticsTests
    {
        private string _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            ConsoleStartup.ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            ConsoleStartup.ResetStatics();
            TestConfig.Release(_config);
        }

        [Test]
        public void Initialize_NothingWrong_WritesNothingToTheUnityLog()
        {
            var registry = new CommandRegistry();

            registry.Initialize();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Startup_AutoexecRan_WritesNothingToTheUnityLog()
        {
            File.WriteAllText(Path.Combine(_config, ConsoleConfig.AutoexecFileName), "clear\n");

            ConsoleStartup.Run(new CommandRegistry(), commandLine: false);

            LogAssert.NoUnexpectedReceived();
        }
    }
}
