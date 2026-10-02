using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace Rubickanov.Input.Tests
{
    public sealed class ActiveDeviceTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;
        private ActiveDevice _device;
        private int _changes;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _device = new ActiveDevice();
            _device.Changed.Subscribe(_ => _changes++);
            _changes = 0;
        }

        public override void TearDown()
        {
            _device.Dispose();
            base.TearDown();
        }

        [Test]
        public void Scheme_PadPressThenKey_TurnsToThePadAndBack()
        {
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys));

            Press(_pad.buttonSouth);
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad));
            Assert.That(_device.Pad, Is.SameAs(_pad));
            Assert.That(_changes, Is.EqualTo(1));

            Press(_keyboard.aKey);
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys));
            Assert.That(_changes, Is.EqualTo(2));
        }

        [Test]
        public void Force_Family_HoldsThePadWhateverIsPressed()
        {
            _device.Force(PadFamily.Nintendo);
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad));
            Assert.That(_device.Family, Is.EqualTo(PadFamily.Nintendo));

            Press(_keyboard.aKey);
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad), "the keys do not take it back");

            _device.Force(null);
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys));
            Assert.That(_device.Family, Is.EqualTo(PadFamily.Xbox));
            Assert.That(_changes, Is.EqualTo(2));
        }

        [Test]
        public void Scheme_StickDrift_DoesNotCountPastActuationDoes()
        {
            Set(_pad.leftStick, new Vector2(0.2f, 0f));
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys), "drift");

            Set(_pad.leftStick, new Vector2(0.9f, 0f));
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad));
        }

        [Test]
        public void Scheme_MouseMoving_DoesNotCountItsButtonsDo()
        {
            Press(_pad.buttonSouth);

            Set(_mouse.delta, new Vector2(40f, 10f));
            Set(_mouse.position, new Vector2(300f, 200f));
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad));

            Press(_mouse.leftButton);
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys));
        }

        [Test]
        public void Pointed_ForcedFamily_LeavesItAlone()
        {
            _device.Force(PadFamily.Nintendo);

            _device.Pointed();

            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad));
            Assert.That(_device.Family, Is.EqualTo(PadFamily.Nintendo));
        }

        [Test]
        public void Pointed_PadInHand_TurnsToTheKeys()
        {
            Press(_pad.buttonSouth);

            _device.Pointed();

            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys));
        }

        [Test]
        public void Scheme_PadInHandRemoved_TurnsToTheKeys()
        {
            Press(_pad.buttonSouth);
            _changes = 0;

            InputSystem.RemoveDevice(_pad);

            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys));
            Assert.That(_changes, Is.EqualTo(1));
        }

        [Test]
        public void Pad_TwoPadsPressedApart_TheLastPressedIsInHand()
        {
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();

            Press(_pad.buttonSouth);
            Assert.That(_device.Family, Is.EqualTo(PadFamily.Xbox));

            currentTime += 1;
            Press(dualShock.buttonSouth);
            Assert.That(_device.Pad, Is.SameAs(dualShock));
            Assert.That(_device.Family, Is.EqualTo(PadFamily.PlayStation));

            currentTime += 1;
            Press(_pad.buttonNorth);
            Assert.That(_device.Pad, Is.SameAs(_pad), "a pad pressed on its own takes the hand back");
        }

        [Test]
        public void Pad_SteamsXboxCopyPressingAlong_NeverTakesTheHand()
        {
            // Steam Input hands a DualShock over as an Xbox pad and Unity still sees the DualShock itself.
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();

            Press(dualShock.buttonSouth);
            currentTime += 0.03;
            Press(_pad.buttonSouth);
            Assert.That(_device.Pad, Is.SameAs(dualShock));
            Assert.That(_device.Family, Is.EqualTo(PadFamily.PlayStation));
            Assert.That(_changes, Is.EqualTo(1));
            Assert.That(_device.EchoNames, Is.EqualTo(new[] { _pad.displayName }));

            currentTime += 2;
            Release(dualShock.buttonSouth);
            Release(_pad.buttonSouth);
            currentTime += 2;
            Press(_pad.buttonEast);
            currentTime += 0.03;
            Press(dualShock.buttonEast);
            Assert.That(_device.Pad, Is.SameAs(dualShock), "the copy pressing first");
            Assert.That(_changes, Is.EqualTo(1));
        }

        [Test]
        public void Pad_SteamsCopyPressingFirst_GivesTheHandToThePad()
        {
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();

            Press(_pad.buttonSouth);
            currentTime += 0.03;
            Press(dualShock.buttonSouth);
            Assert.That(_device.Pad, Is.SameAs(dualShock));
            _changes = 0;

            currentTime += 2;
            Set(_pad.leftStick, new Vector2(0.9f, 0f));
            currentTime += 0.03;
            Set(dualShock.leftStick, new Vector2(0.9f, 0f));
            Assert.That(_device.Pad, Is.SameAs(dualShock));
            Assert.That(_changes, Is.EqualTo(0));
        }

        [Test]
        public void Pad_TwoAlikePadsPressingAsOne_TheOneInHandStays()
        {
            var other = InputSystem.AddDevice<Gamepad>();

            Press(_pad.buttonSouth);
            currentTime += 0.03;
            Press(other.buttonSouth);
            currentTime += 2;
            Press(other.buttonNorth);

            Assert.That(_device.Pad, Is.SameAs(_pad));
            Assert.That(_changes, Is.EqualTo(1));
        }

        [Test]
        public void EchoNames_PadRemoved_ForgetsItsCopy()
        {
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();
            Press(dualShock.buttonSouth);
            currentTime += 0.03;
            Press(_pad.buttonSouth);

            InputSystem.RemoveDevice(dualShock);
            currentTime += 2;
            Press(_pad.buttonNorth);

            Assert.That(_device.Pad, Is.SameAs(_pad));
            Assert.That(_device.EchoNames, Is.Empty);
        }

        [Test]
        public void Family_FamilySource_IsAskedBeforeTheLayout()
        {
            _device.Dispose();
            _device = new ActiveDevice(new IPadFamilySource[] { new Unsure(), new Deck() });

            Press(_pad.buttonSouth);

            Assert.That(_device.Family, Is.EqualTo(PadFamily.SteamDeck));
        }

        [Test]
        public void Family_SourceWithoutAnswer_GoesByTheLayout()
        {
            _device.Dispose();
            _device = new ActiveDevice(new IPadFamilySource[] { new Unsure() });
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();

            Press(dualShock.buttonSouth);

            Assert.That(_device.Family, Is.EqualTo(PadFamily.PlayStation));
        }

        [Test]
        public void Changed_KeyboardLayoutSwitched_IsRaised()
        {
            SetKeyInfo(Key.W, "z");

            Assert.That(_changes, Is.GreaterThanOrEqualTo(1));
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Keys));
        }

        private sealed class Deck : IPadFamilySource
        {
            public PadFamily? FamilyOf(Gamepad pad) => PadFamily.SteamDeck;
        }

        private sealed class Unsure : IPadFamilySource
        {
            public PadFamily? FamilyOf(Gamepad pad) => null;
        }
    }
}
