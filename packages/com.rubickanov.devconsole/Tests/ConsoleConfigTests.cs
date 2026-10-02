using System.IO;
using NUnit.Framework;
using Rubickanov.DevConsole.Commands;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class ConsoleConfigTests
    {
        private string _config = null!;
        private CommandRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _config = TestConfig.Use();
            _registry = new CommandRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey("DevConsole_Aliases");
            PlayerPrefs.DeleteKey("DevConsole_Bindings");
            TestConfig.Release(_config);
        }

        [Test]
        public void Changes_AreWrittenAsConsoleCommands()
        {
            _registry.Aliases.Set("slow", "timescale 0.2");
            _registry.Bindings.Set(new KeyChord(Key.F5, KeyModifiers.Ctrl), "echo \"a b\"");
            _registry.Bindings.Set(new KeyChord(Key.F6), "timescale 1; clear");

            var text = File.ReadAllText(ConsoleConfig.ConfigPath);

            StringAssert.Contains("alias set slow timescale 0.2\n", text);
            StringAssert.Contains("bind set ctrl+F5 echo \"a b\"\n", text);
            StringAssert.Contains("bind set F6 \"timescale 1; clear\"\n", text);
        }

        [Test]
        public void Config_ReadsBackTheSameValues()
        {
            _registry.Aliases.Set("slow", "timescale 0.2");
            _registry.Aliases.Set("reset", "timescale 1; clear");
            _registry.Bindings.Set(new KeyChord(Key.F5, KeyModifiers.Ctrl), "echo \"a b\" c");

            _registry = new CommandRegistry();

            _registry.Aliases.TryResolve("slow", out var slow);
            _registry.Aliases.TryResolve("reset", out var reset);
            _registry.Bindings.Bindings.TryGetValue(new KeyChord(Key.F5, KeyModifiers.Ctrl), out var bound);
            Assert.AreEqual("timescale 0.2", slow);
            Assert.AreEqual("timescale 1; clear", reset);
            Assert.AreEqual("echo \"a b\" c", bound);
        }

        [Test]
        public void Config_EditedByHand_IsRead()
        {
            File.WriteAllText(ConsoleConfig.ConfigPath,
                "# mine\n\nalias set tp teleport $1\nbind set shift+K tp home\nsomething else\nbind set NotAKey x\n");

            _registry.Aliases.TryResolve("tp", out var tp);
            Assert.AreEqual("teleport $1", tp);
            Assert.AreEqual(1, _registry.Bindings.Bindings.Count);
            Assert.AreEqual("tp home", _registry.Bindings.Bindings[new KeyChord(Key.K, KeyModifiers.Shift)]);
        }

        [Test]
        public void PlayerPrefsSave_IsMigratedWhenThereIsNoConfig()
        {
            File.Delete(ConsoleConfig.ConfigPath);
            PlayerPrefs.SetString("DevConsole_Aliases", "{\"keys\":[\"g\"],\"values\":[\"give\"]}");
            PlayerPrefs.SetString("DevConsole_Bindings", "{\"keys\":[\"F5\"],\"values\":[\"timescale 0.5\"]}");

            Assert.IsTrue(_registry.Aliases.TryResolve("g", out _));
            Assert.AreEqual("timescale 0.5", _registry.Bindings.Bindings[new KeyChord(Key.F5)]);

            _registry.Aliases.Set("x", "y");

            var text = File.ReadAllText(ConsoleConfig.ConfigPath);
            StringAssert.Contains("alias set g give", text);
            StringAssert.Contains("bind set F5 timescale 0.5", text);
            Assert.IsFalse(PlayerPrefs.HasKey("DevConsole_Aliases"));
            Assert.IsFalse(PlayerPrefs.HasKey("DevConsole_Bindings"));
        }

        [Test]
        public void Exec_FileThatExecsItself_StopsInsteadOfOverflowing()
        {
            File.WriteAllText(Path.Combine(_config, "loop.cfg"), "# again\nexec loop\n");
            var registry = new CommandRegistry();
            registry.Register("exec", args =>
            {
                if (!ConsoleCommands.RunFile(registry, args[0], out _)) throw new CommandException("missing");
                return null;
            });

            var result = registry.Execute("exec loop");

            Assert.IsTrue(result.Success, result.Message);
            var found = false;
            foreach (var entry in ConsoleLog.Entries)
                found |= entry.Message.Contains("nested deeper");
            Assert.IsTrue(found);
        }

        [Test]
        public void RunAutoexec_RunsTheFileBesideTheConfig()
        {
            File.WriteAllText(Path.Combine(_config, ConsoleConfig.AutoexecFileName), "ping\n# not this\n\nping\n");
            var registry = new CommandRegistry();
            var pings = 0;
            registry.Register("ping", _ => { pings++; return null; });

            ConsoleCommands.RunAutoexec(registry);

            Assert.AreEqual(2, pings);
        }
    }
}
