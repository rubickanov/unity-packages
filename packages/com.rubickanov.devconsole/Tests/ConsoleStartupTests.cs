using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class ConsoleStartupTests
    {
        private string _config = null!;
        private CommandRegistry _registry = null!;
        private readonly List<string> _ran = new();

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            ConsoleStartup.ResetStatics();
            _registry = new CommandRegistry();
            _registry.Register("probe", args => _ran.Add(args[0]));
        }

        [TearDown]
        public void TearDown()
        {
            // Whatever a test left queued must not reach the next one
            StartupCommands.Run(_registry);
            ConsoleStartup.ResetStatics();
            TestConfig.Release(_config);
            _ran.Clear();
        }

        [Test]
        public void Run_CommandLineOn_RunsAutoexecAndThenTheCommandLine()
        {
            File.WriteAllText(Path.Combine(_config, ConsoleConfig.AutoexecFileName), "probe autoexec\n");
            StartupCommands.Enqueue("probe command");

            _registry.Initialize();
            var ran = ConsoleStartup.Run(_registry, commandLine: true);

            Assert.IsTrue(ran);
            CollectionAssert.AreEqual(new[] { "autoexec", "command" }, _ran);
        }

        [Test]
        public void Run_CommandLineOff_RunsAutoexecAndLeavesTheCommandsForTheGame()
        {
            File.WriteAllText(Path.Combine(_config, ConsoleConfig.AutoexecFileName), "probe autoexec\n");
            StartupCommands.Enqueue("probe command");

            ConsoleStartup.Run(_registry, commandLine: false);

            CollectionAssert.AreEqual(new[] { "autoexec" }, _ran);
            CollectionAssert.Contains(StartupCommands.Pending, "probe command");
        }

        [Test]
        public void Run_SecondTime_RunsNothing()
        {
            File.WriteAllText(Path.Combine(_config, ConsoleConfig.AutoexecFileName), "probe autoexec\n");
            ConsoleStartup.Run(_registry, commandLine: true);

            var again = ConsoleStartup.Run(_registry, commandLine: true);

            Assert.IsFalse(again);
            CollectionAssert.AreEqual(new[] { "autoexec" }, _ran);
        }

        [Test]
        public void Run_GameCommandRegisteredBeforeTheFirstFrame_IsThereForAutoexec()
        {
            File.WriteAllText(Path.Combine(_config, ConsoleConfig.AutoexecFileName), "spawn crate\n");
            var spawned = new List<string>();
            _registry.Register("spawn", args => spawned.Add(args[0]));

            ConsoleStartup.Run(_registry, commandLine: true);

            CollectionAssert.AreEqual(new[] { "crate" }, spawned);
        }

        [Test]
        public void Run_AutoexecUnreadable_WarnsAndStillRunsTheCommandLine()
        {
            var path = Path.Combine(_config, ConsoleConfig.AutoexecFileName);
            File.WriteAllText(path, "probe autoexec\n");
            StartupCommands.Enqueue("probe command");
            LogAssert.Expect(LogType.Warning, new Regex("autoexec.cfg could not be read"));

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                ConsoleStartup.Run(_registry, commandLine: true);

            CollectionAssert.AreEqual(new[] { "command" }, _ran);
        }

        [Test]
        public void Initialize_WithAutoexec_LeavesItForTheFirstFrame()
        {
            File.WriteAllText(Path.Combine(_config, ConsoleConfig.AutoexecFileName), "probe autoexec\n");

            _registry.Initialize();

            CollectionAssert.IsEmpty(_ran);
        }
    }
}
