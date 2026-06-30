using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.Config.Tests
{
    [TestFixture]
    public class ConfigDatabaseTests
    {
        private TestDatabase _database;
        private readonly List<TestData> _createdItems = new();

        [SetUp]
        public void SetUp()
        {
            _database = ScriptableObject.CreateInstance<TestDatabase>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _createdItems)
                UnityEngine.Object.DestroyImmediate(item);
            _createdItems.Clear();
            UnityEngine.Object.DestroyImmediate(_database);
        }

        private TestData CreateTestData(string id, int value)
        {
            var data = ScriptableObject.CreateInstance<TestData>();
            data.Id = id;
            data.Value = value;
            _createdItems.Add(data);
            return data;
        }

        [Test]
        public void Get_ExistingId_ReturnsItem()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10),
                CreateTestData("item2", 20)
            });

            var result = _database.Get("item1");

            Assert.IsNotNull(result);
            Assert.AreEqual("item1", result.Id);
            Assert.AreEqual(10, result.Value);
        }

        [Test]
        public void Get_NonExistingId_ReturnsNull()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10)
            });

            var result = _database.Get("nonexistent");

            Assert.IsNull(result);
        }

        [Test]
        public void Get_EmptyDatabase_ReturnsNull()
        {
            var result = _database.Get("any");

            Assert.IsNull(result);
        }

        [Test]
        public void All_ReturnsAllItems()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10),
                CreateTestData("item2", 20),
                CreateTestData("item3", 30)
            });

            var result = _database.All;

            Assert.AreEqual(3, result.Count);
        }

        [Test]
        public void All_EmptyDatabase_ReturnsEmptyList()
        {
            var result = _database.All;

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void All_IsReadOnly()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10)
            });

            Assert.IsInstanceOf<IReadOnlyList<TestData>>(_database.All);
        }

        [Test]
        public void Get_SameIdCalledTwice_ReturnsSameInstance()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10)
            });

            var first = _database.Get("item1");
            var second = _database.Get("item1");

            Assert.AreSame(first, second);
        }

        [Test]
        public void Get_LookupBuiltLazily_DoesNotReflectPostCreationChanges()
        {
            var original = CreateTestData("item1", 10);
            _database.SetItems(new List<TestData> { original });

            Assert.AreSame(original, _database.Get("item1"));

            var replacement = CreateTestData("item1", 999);
            _database.SetItems(new List<TestData>
            {
                replacement,
                CreateTestData("item2", 20)
            });

            Assert.AreSame(original, _database.Get("item1"));
            Assert.IsNull(_database.Get("item2"));
        }

        [Test]
        public void All_PreservesInsertionOrder()
        {
            var a = CreateTestData("a", 1);
            var b = CreateTestData("b", 2);
            var c = CreateTestData("c", 3);
            _database.SetItems(new List<TestData> { c, a, b });

            var all = _database.All;

            Assert.AreEqual(3, all.Count);
            Assert.AreSame(c, all[0]);
            Assert.AreSame(a, all[1]);
            Assert.AreSame(b, all[2]);
        }

        [Test]
        public void Get_DuplicateIds_ThrowsWithIdList()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("dup", 1),
                CreateTestData("unique", 2),
                CreateTestData("dup", 3)
            });

            var ex = Assert.Throws<InvalidOperationException>(() => _database.Get("dup"));

            Assert.That(ex!.Message, Does.Contain("dup"));
        }

        [Test]
        public void Get_NullId_ReturnsNull()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10)
            });

            Assert.IsNull(_database.Get(null!));
        }

        [Test]
        public void Get_EmptyIdItems_AreSkippedNotThrown()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("", 1),
                CreateTestData("real", 2)
            });

            Assert.IsNull(_database.Get(""));
            Assert.AreEqual(2, _database.Get("real")!.Value);
        }

        [Test]
        public void Validate_AllUniqueNonEmptyIds_ReturnsTrue()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("a", 1),
                CreateTestData("b", 2)
            });

            Assert.IsTrue(_database.Validate());
        }

        [Test]
        public void Validate_DuplicateIds_ReturnsFalse()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("dup", 1),
                CreateTestData("dup", 2)
            });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Duplicate Id"));
            Assert.IsFalse(_database.Validate());
        }

        [Test]
        public void Validate_EmptyId_ReturnsFalse()
        {
            _database.SetItems(new List<TestData>
            {
                CreateTestData("", 1)
            });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Empty Id"));
            Assert.IsFalse(_database.Validate());
        }

        [Serializable]
        private class TestData : ConfigBase, IIdentifiable
        {
            public string Id;
            public int Value;

            string IIdentifiable.Id => Id;
        }

        private class TestDatabase : ConfigDatabase<TestData>
        {
            public void SetItems(List<TestData> items)
            {
                var field = typeof(ConfigDatabase<TestData>)
                    .GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(this, items);
            }
        }
    }
}
