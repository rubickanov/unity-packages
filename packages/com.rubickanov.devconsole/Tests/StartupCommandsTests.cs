using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class StartupCommandsTests
    {
        // A failing command is reported to the player log on purpose, so the runner must not take it for a crash.
        // Cleared here rather than after the call: an assertion that throws in between would otherwise leak the flag
        // into every test that follows.
        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        [Test]
        public void Parse_NoFlag_ReturnsEmpty()
        {
            var commands = StartupCommands.Parse(new[] { "game", "-batchmode", "-nographics" });

            Assert.AreEqual(0, commands.Count);
        }

        [Test]
        public void Parse_OneFlag_ReturnsItsCommand()
        {
            var commands = StartupCommands.Parse(new[] { "game", "-command", "net host 7777" });

            CollectionAssert.AreEqual(new[] { "net host 7777" }, commands);
        }

        [Test]
        public void Parse_SeveralFlags_KeepsTheOrderTheyWereGivenIn()
        {
            var commands = StartupCommands.Parse(new[]
            {
                "game", "-command", "net profile lan", "-batchmode", "-command", "net host 7777",
            });

            CollectionAssert.AreEqual(new[] { "net profile lan", "net host 7777" }, commands);
        }

        [Test]
        public void Parse_FlagLastWithNothingAfterIt_IsDropped()
        {
            var commands = StartupCommands.Parse(new[] { "game", "-command" });

            Assert.AreEqual(0, commands.Count);
        }

        [Test]
        public void Parse_CommandThatLooksLikeAFlag_IsStillTakenAsTheValue()
        {
            // The shell decides what one argument is, not this parser: whatever follows the flag is the command
            var commands = StartupCommands.Parse(new[] { "game", "-command", "-nographics" });

            CollectionAssert.AreEqual(new[] { "-nographics" }, commands);
        }

        [Test]
        public void Parse_FlagInAnyCase_IsRecognised()
        {
            var commands = StartupCommands.Parse(new[] { "game", "-Command", "net stats" });

            CollectionAssert.AreEqual(new[] { "net stats" }, commands);
        }

        [Test]
        public void Parse_FlagWithBlankValue_IsDropped()
        {
            var commands = StartupCommands.Parse(new[] { "game", "-command", "", "-command", "   " });

            Assert.AreEqual(0, commands.Count);
        }

        [Test]
        public void Parse_ValueWithSpaces_StaysOneCommandForTheRegistryToTokenize()
        {
            var commands = StartupCommands.Parse(new[] { "game", "-command", "say \"hello world\"" });

            CollectionAssert.AreEqual(new[] { "say \"hello world\"" }, commands);
            CollectionAssert.AreEqual(new[] { "say", "hello world" }, CommandRegistry.Tokenize(commands[0]));
        }

        [Test]
        public void Parse_Null_ReturnsEmpty()
        {
            var commands = StartupCommands.Parse(null);

            Assert.AreEqual(0, commands.Count);
        }

        [Test]
        public void Run_SeveralCommands_ExecutesThemInOrder()
        {
            var registry = new CommandRegistry();
            var seen = new List<string>();
            registry.Register("probe", args => { seen.Add(args.Length > 0 ? args[0] : string.Empty); });

            int ran = StartupCommands.Run(registry, new[] { "probe one", "probe two" });

            Assert.AreEqual(2, ran);
            CollectionAssert.AreEqual(new[] { "one", "two" }, seen);
        }

        [Test]
        public void Run_UnknownCommand_KeepsGoingAndStillCountsIt()
        {
            var registry = new CommandRegistry();
            bool reached = false;
            registry.Register("probe", _ => { reached = true; });

            LogAssert.ignoreFailingMessages = true;
            int ran = StartupCommands.Run(registry, new[] { "nosuchcommand", "probe" });

            Assert.AreEqual(2, ran);
            Assert.IsTrue(reached, "a failing command must not stop the ones after it");
        }

        [Test]
        public void Run_CalledTwice_DrainsTheQueueOnlyOnce()
        {
            var registry = new CommandRegistry();
            int executed = 0;
            registry.Register("probe", _ => { executed++; });
            StartupCommands.Enqueue("probe");

            int first = StartupCommands.Run(registry);
            int second = StartupCommands.Run(registry);

            // The guarantee the whole feature rests on: a scene reload runs the hook again and must start nothing
            Assert.AreEqual(1, first);
            Assert.AreEqual(0, second);
            Assert.AreEqual(1, executed);
        }

        [Test]
        public void Run_NoCommands_RunsNothing()
        {
            var registry = new CommandRegistry();

            int ran = StartupCommands.Run(registry, Array.Empty<string>());

            Assert.AreEqual(0, ran);
        }
    }
}
