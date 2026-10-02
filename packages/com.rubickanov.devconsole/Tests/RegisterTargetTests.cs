using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public partial class RegisterTargetTests
    {
        private CommandRegistry _registry = null!;

        [SetUp]
        public void SetUp() => _registry = new CommandRegistry();

        [Test]
        public void RegisterTarget_InstanceMethod_GetsRegisteredAndInvokedOnTarget()
        {
            var target = new Service { Greeting = "hello" };
            _registry.RegisterTarget(target);

            var result = _registry.Execute("greet world");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("hello, world", result.Message);
        }

        [Test]
        public void RegisterTarget_TwoInstancesOfSameType_LastWins()
        {
            var first = new Service { Greeting = "hello" };
            var second = new Service { Greeting = "hi" };
            _registry.RegisterTarget(first);
            _registry.RegisterTarget(second);

            var result = _registry.Execute("greet world");

            Assert.AreEqual("hi, world", result.Message);
        }

        [Test]
        public void UnregisterTarget_RemovesAllItsCommands()
        {
            var target = new Service { Greeting = "hello" };
            _registry.RegisterTarget(target);

            _registry.UnregisterTarget(target);

            Assert.IsFalse(_registry.Execute("greet world").Success);
        }

        [Test]
        public void UnregisterTarget_OnlyRemovesCommandsForThatTarget()
        {
            var first = new Service { Greeting = "hi" };
            var other = new OtherService();
            _registry.RegisterTarget(first);
            _registry.RegisterTarget(other);

            _registry.UnregisterTarget(first);

            Assert.IsFalse(_registry.Execute("greet world").Success);
            Assert.IsTrue(_registry.Execute("ping").Success);
        }

        [Test]
        public void RegisterTarget_DerivedType_GetsItsBaseTypesCommandsToo()
        {
            _registry.RegisterTarget(new DerivedService());

            Assert.AreEqual("pong", _registry.Execute("ping").Message);
            Assert.AreEqual("derived", _registry.Execute("derived").Message);
        }

        [Test]
        public void RegisterTarget_ObjectWithoutCommands_WarnsAndRegistersNothing()
        {
            var before = _registry.Commands.Count;
            LogAssert.Expect(LogType.Warning, new Regex(@"NoCommands has no instance \[ConsoleCommand\] methods"));

            _registry.RegisterTarget(new NoCommands());

            Assert.AreEqual(before, _registry.Commands.Count);
        }

        [Test]
        public void RegisterTarget_NullTarget_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _registry.RegisterTarget(null!));
        }

        private partial class Service
        {
            public string Greeting = "";

            [ConsoleCommand("greet", "Greet someone")]
            public string Greet(string name) => $"{Greeting}, {name}";
        }

        private partial class OtherService
        {
            [ConsoleCommand("ping", "Ping")]
            public string Ping() => "pong";
        }

        private partial class DerivedService : OtherService
        {
            [ConsoleCommand("derived", "Declared by the derived type")]
            public string Derived() => "derived";
        }

        private sealed class NoCommands
        {
        }
    }
}
