using System.Linq;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace Rubickanov.Input.Tests
{
    public sealed class ControlNamesTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private Gamepad _pad;
        private InputActionAsset _input;
        private ActiveDevice _device;
        private ControlNames _names;
        private int _changes;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _input = TestInput.Create();
            _device = new ActiveDevice();
            _names = new ControlNames(_input, _device);
            _names.Changed.Subscribe(_ => _changes++);
            _changes = 0;
        }

        public override void TearDown()
        {
            _names.Dispose();
            _device.Dispose();
            TestInput.Destroy(_input);
            base.TearDown();
        }

        [Test]
        public void Text_PadPickedUp_TurnsFromTheKeyboardsKeysToThePads()
        {
            Assert.That(_names.Text("Menu/Submit"), Is.EqualTo("ENTER"));
            Assert.That(_names.Text("Gameplay/Move"), Is.EqualTo("WASD"), "up, left, down, right");
            Assert.That(_names.Text("Menu/Tab"), Is.EqualTo("Q E"));
            Assert.That(_names.Text("Gameplay/Look"), Is.EqualTo("MOUSE"));
            Assert.That(_names.Text("Menu/Navigate"), Is.EqualTo("UP LEFT DOWN RIGHT"));

            Press(_pad.buttonSouth);

            Assert.That(_changes, Is.EqualTo(1));
            Assert.That(_names.Text("Menu/Submit"), Is.EqualTo("A"));
            Assert.That(_names.Text("Menu/Cancel"), Is.EqualTo("B"));
            Assert.That(_names.Text("Menu/Tab"), Is.EqualTo("LB RB"));
            Assert.That(_names.Text("Menu/Navigate"), Is.EqualTo("LS"));
            Assert.That(_names.Text("Gameplay/Jump"), Is.EqualTo("A"));
            Assert.That(_names.Text("Menu/Restart"), Is.EqualTo("VIEW"));
        }

        [Test]
        public void Text_Scheme_EitherCanBeAskedFor()
        {
            Assert.That(_names.Text("Menu/Submit", ControlScheme.Pad), Is.EqualTo("A"));
            Assert.That(_names.Text("Menu/Submit", ControlScheme.Keys), Is.EqualTo("ENTER"));
            Assert.That(_names.Text("Gameplay/Zoom", ControlScheme.Pad), Is.EqualTo("D-PAD DOWN D-PAD UP"));
            Assert.That(_names.Text("Gameplay/Sprint", ControlScheme.Pad), Is.EqualTo("RT"));
        }

        [Test]
        public void Keys_InputAction_NamesItForTheDeviceInHand()
        {
            InputAction interact = _input.FindAction("Gameplay/Interact", true);

            Assert.That(_names.Text(interact), Is.EqualTo("E"));
            Press(_pad.buttonSouth);
            Assert.That(_names.Keys(interact)[0].Text, Is.EqualTo("X"));
        }

        [Test]
        public void Action_UnknownName_Throws()
        {
            Assert.That(_names.Action("Jump"), Is.SameAs(_input.FindAction("Gameplay/Jump")));
            Assert.Throws<System.ArgumentException>(() => _names.Action("Gameplay/Fly"));
        }

        [Test]
        public void IsDown_KeyHeld_True()
        {
            KeyName enter = _names.Name("<Keyboard>/enter");
            KeyName south = _names.Name("<Gamepad>/buttonSouth");
            KeyName stickUp = _names.Name("<Gamepad>/leftStick/up");
            Assert.That(_names.IsDown(enter), Is.False);

            Press(_keyboard.enterKey);
            Assert.That(_names.IsDown(enter), Is.True);
            Release(_keyboard.enterKey);
            Assert.That(_names.IsDown(enter), Is.False);

            Press(_pad.buttonSouth);
            Assert.That(_names.IsDown(south), Is.True);
            Set(_pad.leftStick, new Vector2(0f, 0.9f));
            Assert.That(_names.IsDown(stickUp), Is.True);
        }

        [Test]
        public void Text_AzertyKeyboard_NamesZqsd()
        {
            SetKeyInfo(Key.W, "z");
            SetKeyInfo(Key.A, "q");

            Assert.That(_names.Text("Gameplay/Move"), Is.EqualTo("ZQSD"));
        }

        [Test]
        public void Text_Part_NamesOnePartOfAComposite()
        {
            Assert.That(_names.Text("Menu/Navigate", "up"), Is.EqualTo("UP"));
            Assert.That(_names.Text("Menu/Tab", "positive"), Is.EqualTo("E"));

            Press(_pad.buttonSouth);

            Assert.That(_names.Text("Menu/Navigate", "up"), Is.EqualTo("LS UP"), "a way of the stick bound whole");
            Assert.That(_names.Keys("Menu/Navigate", "up")[0].Control, Is.EqualTo("leftStick/up"));
            Assert.That(_names.Text("Menu/Tab", "positive"), Is.EqualTo("RB"));
            Assert.That(_names.Text("Menu/Submit", "up"), Is.Empty, "a button has no ways");
        }

        [Test]
        public void Changed_BindingOverride_RenamesTheKeyAndSaysSo()
        {
            // Unity tells of a rebind once the input's controls are resolved, as they are when its maps are on.
            _input.Enable();

            _input.FindAction("Gameplay/Jump", true).ApplyBindingOverride(0, "<Keyboard>/f");

            Assert.That(_names.Text("Gameplay/Jump"), Is.EqualTo("F"));
            Assert.That(_changes, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Changed_OtherInputsOverride_IsNotRaised()
        {
            InputActionAsset other = TestInput.Create();
            try
            {
                other.Enable();
                other.FindAction("Gameplay/Jump", true).ApplyBindingOverride(0, "<Keyboard>/f");

                Assert.That(_changes, Is.EqualTo(0));
            }
            finally
            {
                TestInput.Destroy(other);
            }
        }

        [Test]
        public void Text_PlayStationPad_NamesItsOwnButtons()
        {
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();

            Press(dualShock.buttonSouth);

            Assert.That(_names.Text("Menu/Submit"), Is.EqualTo("CROSS"));
            Assert.That(_names.Text("Menu/Tab"), Is.EqualTo("L1 R1"));
        }

        [Test]
        public void Fixed_KeyOfTheOtherScheme_IsEmpty()
        {
            Assert.That(_names.Fixed("<Keyboard>/enter"), Is.EqualTo("ENTER"));

            Press(_pad.buttonSouth);

            Assert.That(_names.Fixed("<Keyboard>/enter"), Is.Empty);
        }

        [Test]
        public void Group_ControlSchemes_FoundByTheirDevices()
        {
            Assert.That(_names.Group(ControlScheme.Keys), Is.EqualTo("Keyboard&Mouse"));
            Assert.That(_names.Group(ControlScheme.Pad), Is.EqualTo("Gamepad"));
        }

        [Test]
        public void Text_NoControlSchemes_GoesByTheDeviceOfEachPath()
        {
            InputActionAsset bare = TestInput.Create(schemes: false);
            using var names = new ControlNames(bare, _device);
            try
            {
                Assert.That(names.Group(ControlScheme.Pad), Is.Null);
                Assert.That(names.Text("Gameplay/Move", ControlScheme.Keys), Is.EqualTo("WASD"));
                Assert.That(names.Text("Gameplay/Move", ControlScheme.Pad), Is.EqualTo("LS"));
                Assert.That(names.Text("Menu/Submit", ControlScheme.Keys), Is.EqualTo("ENTER"));
                Assert.That(names.Text("Menu/Tab", ControlScheme.Pad), Is.EqualTo("LB RB"));
            }
            finally
            {
                TestInput.Destroy(bare);
            }
        }

        [Test]
        public void WayOrder_Parts_UpLeftDownRightThenTheRest()
        {
            Assert.That(new[] { "up", "left", "down", "right" }.Select(ControlNames.WayOrder), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(ControlNames.WayOrder("DOWN"), Is.EqualTo(2));
            Assert.That(ControlNames.WayOrder("modifier"), Is.EqualTo(4));
            Assert.That(ControlNames.WayOrder(null), Is.EqualTo(4));
        }
    }
}
