using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class RegisterGroupTests
    {
        private CommandRegistry _registry = null!;

        [SetUp]
        public void SetUp() => _registry = new CommandRegistry();

        [Test]
        public void RegisterGroup_NoArgs_PrintsUsage()
        {
            _registry.RegisterGroup("inv", "Inventory", "Test", g =>
            {
                g.Add("add", _ => null, "Add stuff");
                g.Add("clear", _ => null, "Wipe");
            });

            var result = _registry.Execute("inv");

            Assert.IsTrue(result.Success);
            StringAssert.Contains("Usage", result.Message ?? "");
            StringAssert.Contains("add", result.Message ?? "");
            StringAssert.Contains("clear", result.Message ?? "");
        }

        [Test]
        public void RegisterGroup_UnknownSubcommand_ReturnsError()
        {
            _registry.RegisterGroup("inv", "Inventory", "Test", g => g.Add("add", _ => null));

            var result = _registry.Execute("inv mystery");

            StringAssert.Contains("Unknown subcommand", result.Message ?? "");
        }

        [Test]
        public void RegisterGroup_KnownSubcommand_ReceivesArgsAfterSubcommandName()
        {
            string[] captured = null!;
            _registry.RegisterGroup("inv", "Inventory", "Test", g =>
                g.Add("add", args =>
                {
                    captured = args;
                    return null;
                }));

            _registry.Execute("inv add apple 5");

            CollectionAssert.AreEqual(new[] { "apple", "5" }, captured);
        }

        [Test]
        public void Group_AliasForRegisterGroup_CreatesGroup()
        {
            _registry.Group("inv", "", "Test", g => g.Add("add", _ => "ok"));

            var result = _registry.Execute("inv add");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("ok", result.Message);
        }

        [Test]
        public void TypedBuilder_ActionWithIntArg_ParsesAndInvokes()
        {
            int captured = 0;
            _registry.Group("inv", "", "Test", g =>
                g.Add<int>("add", n => captured = n));

            var result = _registry.Execute("inv add 7");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(7, captured);
        }

        [Test]
        public void TypedBuilder_TwoArgs_ParsesBoth()
        {
            string capturedFirst = null!;
            int capturedSecond = 0;
            _registry.Group("inv", "", "Test", g =>
                g.Add<string, int>("add", (s, n) =>
                {
                    capturedFirst = s;
                    capturedSecond = n;
                }));

            var result = _registry.Execute("inv add apple 3");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("apple", capturedFirst);
            Assert.AreEqual(3, capturedSecond);
        }

        [Test]
        public void TypedBuilder_MissingArg_ReturnsError()
        {
            _registry.Group("inv", "", "Test", g => g.Add<int>("add", _ => { }));

            var result = _registry.Execute("inv add");

            StringAssert.Contains("Missing required argument", result.Message ?? "");
        }

        [Test]
        public void TypedBuilder_UnparseableArg_ReturnsError()
        {
            _registry.Group("inv", "", "Test", g => g.Add<int>("add", _ => { }));

            var result = _registry.Execute("inv add notanumber");

            StringAssert.Contains("Cannot parse", result.Message ?? "");
        }

        [Test]
        public void TypedBuilder_UsesRegisteredDefaultProvider()
        {
            var provider = new RecordingProvider();
            _registry.RegisterDefaultProvider<int>(provider);
            _registry.Group("inv", "", "Test", g => g.Add<int>("add", _ => { }));

            var results = new System.Collections.Generic.List<string>();
            _registry.GetSuggestions("inv add ", results);

            Assert.IsTrue(provider.Called);
        }

        private class RecordingProvider : IAutoCompleteProvider
        {
            public bool Called;
            public string Hint => "<recorded>";

            public void GetSuggestions(string partial, System.Collections.Generic.List<string> results)
            {
                Called = true;
            }
        }
    }
}
