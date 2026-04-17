using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class RegisterTargetTests
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
        public void RegisterTarget_NullTarget_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _registry.RegisterTarget(null!));
        }

        private class Service
        {
            public string Greeting = "";

            [ConsoleCommand("greet", "Greet someone")]
            public string Greet(string name) => $"{Greeting}, {name}";
        }

        private class OtherService
        {
            [ConsoleCommand("ping", "Ping")]
            public string Ping() => "pong";
        }
    }
}
