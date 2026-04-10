using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

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
            // Arrange
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10),
                CreateTestData("item2", 20)
            });

            // Act
            var result = _database.Get("item1");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("item1", result.Id);
            Assert.AreEqual(10, result.Value);
        }

        [Test]
        public void Get_NonExistingId_ReturnsNull()
        {
            // Arrange
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10)
            });

            // Act
            var result = _database.Get("nonexistent");

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void Get_EmptyDatabase_ReturnsNull()
        {
            // Act
            var result = _database.Get("any");

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void All_ReturnsAllItems()
        {
            // Arrange
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10),
                CreateTestData("item2", 20),
                CreateTestData("item3", 30)
            });

            // Act
            var result = _database.All;

            // Assert
            Assert.AreEqual(3, result.Count);
        }

        [Test]
        public void All_EmptyDatabase_ReturnsEmptyList()
        {
            // Act
            var result = _database.All;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void All_IsReadOnly()
        {
            // Arrange
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10)
            });

            // Act & Assert
            Assert.IsInstanceOf<IReadOnlyList<TestData>>(_database.All);
        }

        [Test]
        public void Get_SameIdCalledTwice_ReturnsSameInstance()
        {
            // Arrange
            _database.SetItems(new List<TestData>
            {
                CreateTestData("item1", 10)
            });

            // Act
            var first = _database.Get("item1");
            var second = _database.Get("item1");

            // Assert
            Assert.AreSame(first, second);
        }

        [Test]
        public void Get_LookupBuiltLazily_DoesNotReflectPostCreationChanges()
        {
            // Arrange
            var original = CreateTestData("item1", 10);
            _database.SetItems(new List<TestData> { original });

            // Force lookup dictionary to be built on first Get call.
            Assert.AreSame(original, _database.Get("item1"));

            // Act — swap the backing list with a completely different set of items.
            var replacement = CreateTestData("item1", 999);
            _database.SetItems(new List<TestData>
            {
                replacement,
                CreateTestData("item2", 20)
            });

            // Assert — the cached lookup still returns the original instance
            // and is unaware of the new "item2" entry. Locks in the documented
            // "lazy, cache-once" behavior of ConfigDatabase.Get.
            Assert.AreSame(original, _database.Get("item1"));
            Assert.IsNull(_database.Get("item2"));
        }

        [Test]
        public void All_PreservesInsertionOrder()
        {
            // Arrange
            var a = CreateTestData("a", 1);
            var b = CreateTestData("b", 2);
            var c = CreateTestData("c", 3);
            _database.SetItems(new List<TestData> { c, a, b });

            // Act
            var all = _database.All;

            // Assert
            Assert.AreEqual(3, all.Count);
            Assert.AreSame(c, all[0]);
            Assert.AreSame(a, all[1]);
            Assert.AreSame(b, all[2]);
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
