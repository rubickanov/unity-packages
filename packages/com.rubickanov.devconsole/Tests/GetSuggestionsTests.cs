using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class GetSuggestionsTests
    {
        private CommandRegistry _registry = null!;
        private List<string> _results = null!;

        [SetUp]
        public void SetUp()
        {
            _registry = new CommandRegistry();
            _results = new List<string>();
        }

        [Test]
        public void GetSuggestions_EmptyInput_ListsAllCommandsUpToMax()
        {
            _registry.Register("alpha", _ => null);
            _registry.Register("beta", _ => null);
            _registry.Register("gamma", _ => null);

            _registry.GetSuggestions("", _results, maxResults: 10);

            CollectionAssert.AreEquivalent(new[] { "alpha", "beta", "gamma" }, _results);
        }

        [Test]
        public void GetSuggestions_PartialPrefix_FiltersByCaseInsensitivePrefix()
        {
            _registry.Register("apple", _ => null);
            _registry.Register("apricot", _ => null);
            _registry.Register("banana", _ => null);

            _registry.GetSuggestions("ap", _results);

            CollectionAssert.AreEquivalent(new[] { "apple", "apricot" }, _results);
        }

        [Test]
        public void GetSuggestions_TrimsToMaxResults()
        {
            for (int i = 0; i < 20; i++) _registry.Register($"cmd{i:D2}", _ => null);

            _registry.GetSuggestions("", _results, maxResults: 5);

            Assert.AreEqual(5, _results.Count);
        }

        [Test]
        public void GetSuggestions_SubcommandAware_SuggestsSubcommandNames()
        {
            _registry.Group("inv", "", "Test", g =>
            {
                g.Add("add", _ => null);
                g.Add("remove", _ => null);
                g.Add("clear", _ => null);
            });

            _registry.GetSuggestions("inv ", _results);

            CollectionAssert.AreEquivalent(new[] { "add", "remove", "clear" }, _results);
        }

        [Test]
        public void GetSuggestions_SubcommandPartialName_FiltersSubcommands()
        {
            _registry.Group("inv", "", "Test", g =>
            {
                g.Add("add", _ => null);
                g.Add("remove", _ => null);
            });

            _registry.GetSuggestions("inv re", _results);

            CollectionAssert.AreEqual(new[] { "remove" }, _results);
        }

        [Test]
        public void GetSuggestions_DefaultProviderForType_AppliedToAttributedCommandParam()
        {
            var provider = new ListProvider("foo", "bar", "baz");
            _registry.RegisterDefaultProvider<string>(provider);
            _registry.Group("say", "", "Test", g => g.Add<string>("word", _ => { }));

            _registry.GetSuggestions("say word ", _results);

            CollectionAssert.AreEquivalent(new[] { "foo", "bar", "baz" }, _results);
        }

        private class ListProvider : IAutoCompleteProvider
        {
            private readonly string[] _items;
            public ListProvider(params string[] items) => _items = items;

            public string Hint => "<list>";
            public void GetSuggestions(string partial, List<string> results)
            {
                foreach (var item in _items)
                {
                    if (string.IsNullOrEmpty(partial) ||
                        item.StartsWith(partial, System.StringComparison.OrdinalIgnoreCase))
                        results.Add(item);
                }
            }
        }
    }
}
