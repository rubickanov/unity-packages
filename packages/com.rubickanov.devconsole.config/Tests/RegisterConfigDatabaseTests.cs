using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.Config;
using UnityEngine;

namespace Rubickanov.DevConsole.Config.Tests
{
    [TestFixture]
    public class RegisterConfigDatabaseTests
    {
        private CommandRegistry _registry = null!;
        private TestDatabase _db = null!;
        private readonly List<ScriptableObject> _created = new();

        [SetUp]
        public void SetUp()
        {
            _registry = new CommandRegistry();
            _db = ScriptableObject.CreateInstance<TestDatabase>();
            _created.Add(_db);
            _db.SetItems(new List<TestData>
            {
                CreateItem<TestData>("apple"),
                CreateItem<TestData>("banana"),
                CreateItem<TestData>("blueberry")
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _created)
                Object.DestroyImmediate(item);
            _created.Clear();
        }

        [Test]
        public void RegisterConfigDatabase_KnownId_ParsesToItem()
        {
            _registry.RegisterConfigDatabase(_db);

            var ok = _registry.TryParseArg("apple", typeof(TestData), out var result);

            Assert.IsTrue(ok);
            Assert.AreSame(_db.Get("apple"), result);
        }

        [Test]
        public void RegisterConfigDatabase_UnknownId_ReturnsFalse()
        {
            _registry.RegisterConfigDatabase(_db);

            var ok = _registry.TryParseArg("missing", typeof(TestData), out var result);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
        }

        [Test]
        public void RegisterConfigDatabase_AutocompletePrefix_ReturnsMatchingIds()
        {
            _registry.RegisterConfigDatabase(_db);
            _registry.Group("inv", "", "Test", g => g.Add<TestData>("add", _ => { }));

            var results = new List<string>();
            _registry.GetSuggestions("inv add b", results);

            CollectionAssert.AreEquivalent(new[] { "banana", "blueberry" }, results);
        }

        [Test]
        public void RegisterConfigDatabase_TypedBuilderInvocation_ReceivesParsedItem()
        {
            _registry.RegisterConfigDatabase(_db);
            TestData? captured = null;
            _registry.Group("inv", "", "Test", g =>
                g.Add<TestData>("add", item => captured = item));

            var result = _registry.Execute("inv add banana");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreSame(_db.Get("banana"), captured);
        }

        [Test]
        public void RegisterConfigDatabases_MultipleDatabases_ParsersStackIndependently()
        {
            var second = ScriptableObject.CreateInstance<OtherDatabase>();
            _created.Add(second);
            second.SetItems(new List<OtherData>
            {
                CreateItem<OtherData>("alpha"),
                CreateItem<OtherData>("beta")
            });

            _registry.RegisterConfigDatabases(_db, second);

            Assert.IsTrue(_registry.TryParseArg("apple", typeof(TestData), out var fruit));
            Assert.AreSame(_db.Get("apple"), fruit);

            Assert.IsTrue(_registry.TryParseArg("alpha", typeof(OtherData), out var other));
            Assert.AreSame(second.Get("alpha"), other);
        }

        private T CreateItem<T>(string id) where T : ScriptableObject
        {
            var data = ScriptableObject.CreateInstance<T>();
            switch (data)
            {
                case TestData t: t.Id = id; break;
                case OtherData o: o.Id = id; break;
            }
            _created.Add(data);
            return data;
        }

        private class TestData : ConfigBase, IIdentifiable
        {
            public string Id = "";
            string IIdentifiable.Id => Id;
        }

        private class TestDatabase : ConfigDatabase<TestData>
        {
            public void SetItems(List<TestData> items)
            {
                typeof(ConfigDatabase<TestData>)
                    .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
                    !.SetValue(this, items);
            }
        }

        private class OtherData : ConfigBase, IIdentifiable
        {
            public string Id = "";
            string IIdentifiable.Id => Id;
        }

        private class OtherDatabase : ConfigDatabase<OtherData>
        {
            public void SetItems(List<OtherData> items)
            {
                typeof(ConfigDatabase<OtherData>)
                    .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
                    !.SetValue(this, items);
            }
        }
    }
}
