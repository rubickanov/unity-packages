using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Rubickanov.Input.Tests
{
    public sealed class FedDevicesTests : InputTestFixture
    {
        private FedDevices _fed;

        public override void Setup()
        {
            base.Setup();
            _fed = new FedDevices();
        }

        public override void TearDown()
        {
            _fed.Dispose();
            base.TearDown();
        }

        [Test]
        public void Set_GuestPadPressed_JoinsAPlayerAndDrivesTheirAction()
        {
            Gamepad guest = _fed.AddPad("Remote Play guest");
            using var players = new LocalPlayers<InputActionAsset>(() => TestInput.Create()) { Joining = true };

            Feed(guest, new GamepadState(GamepadButton.South));
            Feed(guest, default);
            currentTime += 0.3;
            InputSystem.Update();
            LocalPlayer<InputActionAsset> player = players.OwnerOf(guest);
            Assert.That(player, Is.Not.Null);

            Feed(guest, new GamepadState(GamepadButton.South));

            Assert.That(player.Input.FindAction("Gameplay/Jump", true).IsPressed(), Is.True);
            Assert.That(_fed.IsFed(guest), Is.True);
        }

        [Test]
        public void SetKey_GuestKeyboard_DrivesAnActionAndKeepsTheOtherKeys()
        {
            Keyboard guest = _fed.AddKeyboard("Remote Play guest keys");
            InputActionAsset input = TestInput.Create();
            input.devices = new InputDevice[] { guest };
            input.Enable();
            try
            {
                _fed.SetKey(guest, Key.W, true);
                _fed.SetKey(guest, Key.Space, true);
                InputSystem.Update();
                Assert.That(input.FindAction("Gameplay/Jump", true).IsPressed(), Is.True);

                _fed.SetKey(guest, Key.Space, false);
                InputSystem.Update();

                Assert.That(input.FindAction("Gameplay/Jump", true).IsPressed(), Is.False);
                Assert.That(guest.wKey.isPressed, Is.True);
            }
            finally
            {
                TestInput.Destroy(input);
            }
        }

        [Test]
        public void Move_GuestMouse_MovesAndPresses()
        {
            Mouse guest = _fed.AddMouse("Remote Play guest mouse");

            _fed.SetPosition(guest, new Vector2(100f, 50f));
            _fed.Move(guest, new Vector2(3f, 1f));
            _fed.SetButton(guest, MouseButton.Left, true);
            InputSystem.Update();

            Assert.That(guest.position.ReadValue(), Is.EqualTo(new Vector2(100f, 50f)));
            Assert.That(guest.delta.ReadValue(), Is.EqualTo(new Vector2(3f, 1f)));
            Assert.That(guest.leftButton.isPressed, Is.True);
        }

        [Test]
        public void Add_GuestDevices_LeaveTheHostsCurrent()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            var pad = InputSystem.AddDevice<Gamepad>();

            _fed.AddKeyboard("guest keys");
            _fed.AddMouse("guest mouse");
            _fed.AddPad("guest pad");

            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(Mouse.current, Is.SameAs(mouse));
            Assert.That(Gamepad.current, Is.SameAs(pad));
        }

        [Test]
        public void Dispose_FedDevices_LeaveTheInputSystem()
        {
            Gamepad pad = _fed.AddPad("guest");
            Keyboard keyboard = _fed.AddKeyboard("guest keys");

            _fed.Dispose();

            Assert.That(pad.added || keyboard.added, Is.False);
            Assert.That(_fed.Devices, Is.Empty);
        }

        [Test]
        public void Set_DeviceNotFed_Throws()
        {
            var pad = InputSystem.AddDevice<Gamepad>();

            Assert.Throws<ArgumentException>(() => _fed.Set(pad, default));
        }

        private void Feed(Gamepad pad, GamepadState state)
        {
            _fed.Set(pad, state);
            InputSystem.Update();
        }
    }
}
