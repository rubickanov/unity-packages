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

        [SetUp]
        public void SetUp()
        {
            _database = ScriptableObject.CreateInstance<TestDatabase>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_database);
        }

        [Test]
        public void Get_ExistingId_ReturnsItem()
        {
            // Arrange
            _database.SetItems(new List<TestData>
            {
                new() { Id = "item1", Value = 10 },
                new() { Id = "item2", Value = 20 }
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
                new() { Id = "item1", Value = 10 }
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
                new() { Id = "item1", Value = 10 },
                new() { Id = "item2", Value = 20 },
                new() { Id = "item3", Value = 30 }
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
                new() { Id = "item1", Value = 10 }
            });

            // Act & Assert
            Assert.IsInstanceOf<IReadOnlyList<TestData>>(_database.All);
        }

        [Serializable]
        private class TestData : IIdentifiable
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
