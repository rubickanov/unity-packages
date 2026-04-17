using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class ExecuteTests
    {
        private CommandRegistry _registry = null!;

        [SetUp]
        public void SetUp() => _registry = new CommandRegistry();

        [Test]
        public void Execute_EmptyInput_ReturnsError()
        {
            var result = _registry.Execute("");

            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Execute_UnknownCommand_ReturnsError()
        {
            var result = _registry.Execute("does_not_exist");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Unknown command", result.Message ?? "");
        }

        [Test]
        public void Execute_RegisteredCommand_RunsHandlerAndReturnsMessage()
        {
            _registry.Register("ping", _ => "pong");

            var result = _registry.Execute("ping");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("pong", result.Message);
        }

        [Test]
        public void Execute_PassesArgsToHandler()
        {
            string[] received = null!;
            _registry.Register("echo", args =>
            {
                received = args;
                return null;
            });

            _registry.Execute("echo a b c");

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, received);
        }

        [Test]
        public void Execute_HandlerThrows_ReturnsErrorWithMessage()
        {
            _registry.Register("boom", _ => throw new System.InvalidOperationException("kaboom"));

            var result = _registry.Execute("boom");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("kaboom", result.Message ?? "");
        }

        [Test]
        public void Execute_PreExecuteFilterReturnsValue_OverridesHandler()
        {
            _registry.Register("ping", _ => "pong");
            _registry.PreExecuteFilter = (_, _) =>
                CommandRegistry.ExecutionResult.Error("blocked");

            var result = _registry.Execute("ping");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("blocked", result.Message);
        }

        [Test]
        public void Execute_PreExecuteFilterReturnsNull_FallsThroughToHandler()
        {
            _registry.Register("ping", _ => "pong");
            _registry.PreExecuteFilter = (_, _) => null;

            var result = _registry.Execute("ping");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("pong", result.Message);
        }

        [Test]
        public void Register_NullHandler_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => _registry.Register("x", (System.Func<string[], string?>)null!));
        }

        [Test]
        public void Register_EmptyName_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => _registry.Register("", _ => null));
        }

        [Test]
        public void Register_WhitespaceName_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => _registry.Register("   ", _ => null));
        }

        [Test]
        public void Unregister_RemovesCommand()
        {
            _registry.Register("ping", _ => "pong");

            Assert.IsTrue(_registry.Unregister("ping"));
            Assert.IsFalse(_registry.Execute("ping").Success);
        }
    }
}
