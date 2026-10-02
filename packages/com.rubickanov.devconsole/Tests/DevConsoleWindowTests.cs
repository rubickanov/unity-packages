using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.DevConsole.Tests
{
    // Edit mode calls no Awake or OnDestroy, so the tests run the window's Attach and Detach themselves.
    [TestFixture]
    public class DevConsoleWindowTests
    {
        private GameObject _host = null!;
        private DevConsoleWindow _window = null!;
        private readonly List<bool> _toggles = new();

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("[DevConsole] test");
            _window = _host.AddComponent<DevConsoleWindow>();
            _window.Attach();
            DevConsoleWindow.Toggled += OnToggled;
        }

        [TearDown]
        public void TearDown()
        {
            DevConsoleWindow.Toggled -= OnToggled;
            _window.Detach();
            Object.DestroyImmediate(_host);
            _toggles.Clear();
        }

        private void OnToggled(bool open) => _toggles.Add(open);

        [Test]
        public void SetOpen_TrueThenFalse_RaisesToggledEachTimeAndReportsIsOpen()
        {
            _window.SetOpen(true);
            var openWhileOpen = DevConsoleWindow.IsOpen;
            _window.SetOpen(false);

            Assert.IsTrue(openWhileOpen);
            Assert.IsFalse(DevConsoleWindow.IsOpen);
            CollectionAssert.AreEqual(new[] { true, false }, _toggles);
        }

        [Test]
        public void SetOpen_SameState_DoesNotRaiseToggled()
        {
            _window.SetOpen(false);

            CollectionAssert.IsEmpty(_toggles);
        }

        [Test]
        public void Detach_WhileOpen_RaisesToggledFalse()
        {
            _window.SetOpen(true);

            _window.Detach();

            CollectionAssert.AreEqual(new[] { true, false }, _toggles);
            Assert.IsFalse(DevConsoleWindow.IsOpen);
            Assert.IsNull(DevConsoleWindow.Instance);
        }

        [Test]
        public void Detach_WhileClosed_DoesNotRaiseToggled()
        {
            _window.Detach();

            CollectionAssert.IsEmpty(_toggles);
        }
    }
}
