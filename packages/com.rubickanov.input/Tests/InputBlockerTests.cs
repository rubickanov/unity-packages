using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Rubickanov.Input.Tests
{
    public sealed class InputBlockerTests
    {
        private static readonly InputScope Gameplay = InputScope.Of("Gameplay");

        private InputActionAsset _input;
        private InputActionMap _gameplay;
        private InputActionMap _menu;
        private InputBlocker _blocker;

        [SetUp]
        public void SetUp()
        {
            _input = TestInput.Create();
            _gameplay = _input.FindActionMap("Gameplay", true);
            _menu = _input.FindActionMap("Menu", true);
            _blocker = new InputBlocker(_input);
        }

        [TearDown]
        public void TearDown()
        {
            _blocker.Dispose();
            TestInput.Destroy(_input);
        }

        [Test]
        public void Constructor_NoReason_EveryMapIsOn()
        {
            Assert.That(_gameplay.enabled && _menu.enabled, Is.True);
            Assert.That(_blocker.IsBlocked, Is.False);
        }

        [Test]
        public void Set_NoScope_BlocksEveryMap()
        {
            _blocker.Set("console", true);

            Assert.That(_blocker.IsBlocked, Is.True);
            Assert.That(_gameplay.enabled || _menu.enabled, Is.False);
            Assert.That(_blocker.IsMapBlocked(_menu), Is.True);
        }

        [Test]
        public void Set_Scope_BlocksOnlyItsMaps()
        {
            _blocker.Set("screen", true, Gameplay);

            Assert.That(_gameplay.enabled, Is.False);
            Assert.That(_menu.enabled, Is.True, "the menus still work");
            Assert.That(_blocker.IsMapBlocked(_gameplay), Is.True);
            Assert.That(_blocker.IsMapBlocked(_menu), Is.False);
        }

        [Test]
        public void Set_OneOfTwoReasonsLifted_TheOtherKeepsBlocking()
        {
            _blocker.Set("console", true);
            _blocker.Set("menu", true);

            _blocker.Set("console", false);

            Assert.That(_blocker.IsBlocked, Is.True);
            Assert.That(_gameplay.enabled, Is.False);

            _blocker.Set("menu", false);

            Assert.That(_blocker.IsBlocked, Is.False);
            Assert.That(_gameplay.enabled, Is.True);
        }

        [Test]
        public void Set_WiderReasonLifted_NarrowerKeepsItsMaps()
        {
            _blocker.Set("screen", true, Gameplay);
            _blocker.Set("console", true, InputScope.All);
            Assert.That(_menu.enabled, Is.False);

            _blocker.Set("console", false);

            Assert.That(_menu.enabled, Is.True);
            Assert.That(_gameplay.enabled, Is.False, "the screen still holds the gameplay");
        }

        [Test]
        public void Set_SameReasonAgain_ReplacesItsScope()
        {
            _blocker.Set("mode", true, InputScope.Of("Menu"));

            _blocker.Set("mode", true, Gameplay);

            Assert.That(_menu.enabled, Is.True);
            Assert.That(_gameplay.enabled, Is.False);
        }

        [Test]
        public void Changed_ManyReasons_RaisedOnlyWhenBlockedFlips()
        {
            int raised = 0;
            _blocker.Changed += _ => raised++;

            _blocker.Set("console", true);
            _blocker.Set("menu", true, Gameplay);
            _blocker.Set("console", false);
            _blocker.Set("menu", false);

            Assert.That(raised, Is.EqualTo(2));
        }

        [Test]
        public void Constructor_GeneratedStyleCollection_FindsTheMapsThroughTheActions()
        {
            var wrapper = new Wrapper(_input);
            using var blocker = new InputBlocker(wrapper);

            blocker.Set("console", true, InputScope.Of("menu"));

            Assert.That(_menu.enabled, Is.False, "map names are matched case aside");
            Assert.That(_gameplay.enabled, Is.True);
        }

        [Test]
        public void Dispose_TurnsEveryMapOff()
        {
            _blocker.Dispose();

            Assert.That(_gameplay.enabled || _menu.enabled, Is.False);
        }

        // As a class generated from an .inputactions file is: an IInputActionCollection2 over the asset.
        private sealed class Wrapper : IInputActionCollection2
        {
            private readonly InputActionAsset _asset;

            public Wrapper(InputActionAsset asset) => _asset = asset;

            public InputBinding? bindingMask { get => _asset.bindingMask; set => _asset.bindingMask = value; }
            public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? devices
            {
                get => _asset.devices;
                set => _asset.devices = value;
            }

            public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputControlScheme> controlSchemes => _asset.controlSchemes;
            public System.Collections.Generic.IEnumerable<InputBinding> bindings => _asset.bindings;
            public bool Contains(InputAction action) => _asset.Contains(action);
            public void Enable() => _asset.Enable();
            public void Disable() => _asset.Disable();

            public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false) =>
                _asset.FindAction(actionNameOrId, throwIfNotFound);

            public int FindBinding(InputBinding mask, out InputAction action) => _asset.FindBinding(mask, out action);

            public System.Collections.Generic.IEnumerator<InputAction> GetEnumerator() => _asset.GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
