using NUnit.Framework;
using Rubickanov.DevConsole.Commands;
using UnityEngine;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class SceneCommandsTests
    {
        private GameObject _root = null!;
        private Transform _child = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Player");
            _child = new GameObject("Weapon").transform;
            _child.SetParent(_root.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [TestCase("weapon", true)]
        [TestCase("Player/Weapon", true)]
        [TestCase("layer/Weapon", false)]
        [TestCase("Weap", false)]
        public void Matches_NameOrPathEnd(string query, bool expected)
        {
            Assert.AreEqual(expected, SceneCommands.Matches(_child, query));
        }
    }
}
