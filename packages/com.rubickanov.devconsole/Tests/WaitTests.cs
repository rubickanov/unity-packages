using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rubickanov.DevConsole.Commands;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class WaitTests
    {
        private CommandRegistry _registry = null!;
        private List<string> _ran = null!;
        private string _config = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            DeferredCommands.Clear();
            _registry = new CommandRegistry();
            ConsoleCommands.Register(_registry);
            _ran = new List<string>();
            _registry.Register("a", _ => { _ran.Add("a"); return null; });
            _registry.Register("b", _ => { _ran.Add("b"); return null; });
            _registry.Register("c", _ => { _ran.Add("c"); return null; });
        }

        [TearDown]
        public void TearDown()
        {
            DeferredCommands.Clear();
            TestConfig.Release(_config);
        }

        [Test]
        public void Wait_DefersTheRestOfTheChainByFrames()
        {
            _registry.Execute("a; wait 2; b; c");
            CollectionAssert.AreEqual(new[] { "a" }, _ran);

            DeferredCommands.Tick();
            CollectionAssert.AreEqual(new[] { "a" }, _ran);

            DeferredCommands.Tick();
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _ran);
            Assert.AreEqual(0, DeferredCommands.Count);
        }

        [Test]
        public void Wait_Twice_WaitsAgainAfterTheFirst()
        {
            _registry.Execute("a; wait 1; b; wait 1; c");

            DeferredCommands.Tick();
            CollectionAssert.AreEqual(new[] { "a", "b" }, _ran);

            DeferredCommands.Tick();
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _ran);
        }

        [Test]
        public void Wait_InAnExecFile_DefersTheRestOfTheFile()
        {
            File.WriteAllText(Path.Combine(_config, "slow.cfg"), "a\nwait 1\n# skipped\nb\nc\n");

            ConsoleCommands.RunFile(_registry, "slow", out _);
            CollectionAssert.AreEqual(new[] { "a" }, _ran);

            DeferredCommands.Tick();
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _ran);
        }

        [Test]
        public void Wait_RefusedByTheFilter_LetsTheRestRunNow()
        {
            _registry.PreExecuteFilter = (cmd, _) =>
                cmd.Name == "wait" ? CommandRegistry.ExecutionResult.Error("no") : null;

            _registry.Execute("a; wait 5; b");

            CollectionAssert.AreEqual(new[] { "a", "b" }, _ran);
            Assert.AreEqual(0, DeferredCommands.Count);
        }

        [Test]
        public void Wait_BadCount_IsAnError()
        {
            Assert.IsFalse(_registry.Execute("wait 0").Success);
            Assert.IsFalse(_registry.Execute("wait x").Success);
        }

        [Test]
        public void Wait_Alone_LeavesNothingBehind()
        {
            Assert.IsTrue(_registry.Execute("wait 3").Success);
            _registry.Execute("a; b");

            CollectionAssert.AreEqual(new[] { "a", "b" }, _ran);
            Assert.AreEqual(0, DeferredCommands.Count);
        }
    }
}
