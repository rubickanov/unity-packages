using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;

namespace Rubickanov.Input.Tests
{
    public sealed class LocalPlayersTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;
        private Gamepad _otherPad;
        private LocalPlayers<InputActionAsset> _players;
        private readonly List<string> _events = new();

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _otherPad = InputSystem.AddDevice<Gamepad>();
            _players = Create(new LocalPlayersOptions());
            _events.Clear();
        }

        public override void TearDown()
        {
            _players.Dispose();
            base.TearDown();
        }

        [Test]
        public void Joining_PadButton_SeatsAPlayerOnThatPadAlone()
        {
            _players.Joining = true;

            Tap(_pad.buttonSouth);

            Assert.That(_players.Players.Count, Is.EqualTo(1));
            LocalPlayer<InputActionAsset> player = _players.Players[0];
            Assert.That(player.Seat, Is.EqualTo(0));
            Assert.That(player.Devices, Is.EqualTo(new InputDevice[] { _pad }));
            Assert.That(player.Input.devices.Value.ToArray(), Is.EqualTo(new InputDevice[] { _pad }));
            Assert.That(player.Device.Scheme, Is.EqualTo(ControlScheme.Pad), "the joining press is in hand");
            Assert.That(_events, Is.EqualTo(new[] { "joined P1" }));
        }

        [Test]
        public void Joining_Key_SeatsTheKeyboardWithTheMouse()
        {
            _players.Joining = true;

            Tap(_keyboard.enterKey);

            Assert.That(_players.Players.Count, Is.EqualTo(1));
            Assert.That(_players.Players[0].Devices, Is.EqualTo(new InputDevice[] { _keyboard, _mouse }));
            Assert.That(_players.Players[0].Device.Scheme, Is.EqualTo(ControlScheme.Keys));
        }

        [Test]
        public void Joining_StickDriftOrMouseMoving_SeatsNoOne()
        {
            _players.Joining = true;

            Set(_pad.leftStick, new UnityEngine.Vector2(0.9f, 0f));
            Set(_mouse.delta, new UnityEngine.Vector2(40f, 0f));
            Wait();

            Assert.That(_players.Players, Is.Empty);
        }

        [Test]
        public void Joining_Off_SeatsNoOne()
        {
            Tap(_pad.buttonSouth);
            Tap(_keyboard.enterKey);

            Assert.That(_players.Players, Is.Empty);
        }

        [Test]
        public void Joining_EverySeatTaken_SeatsNoOneMore()
        {
            _players.Dispose();
            _players = Create(new LocalPlayersOptions { MaxPlayers = 2 });
            _players.Joining = true;

            Tap(_pad.buttonSouth);
            Tap(_otherPad.buttonSouth);
            Tap(_keyboard.enterKey);

            Assert.That(_players.Players.Count, Is.EqualTo(2));
            Assert.That(_players.OwnerOf(_keyboard), Is.Null);
            Assert.That(_players.Join(_keyboard), Is.Null);
        }

        [Test]
        public void Leave_MiddleSeat_IsTheNextTaken()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            Tap(_otherPad.buttonSouth);
            Tap(_keyboard.enterKey);
            LocalPlayer<InputActionAsset> second = _players.Players[1];

            _players.Leave(second);
            Tap(_otherPad.buttonEast);

            Assert.That(_players.Players.Count, Is.EqualTo(3));
            Assert.That(_players.OwnerOf(_otherPad).Seat, Is.EqualTo(1));
            Assert.That(second.Input == null, Is.True, "the left player's input is destroyed");
            Assert.That(_events, Does.Contain("left P2"));
        }

        [Test]
        public void Joining_SteamsXboxCopyPressingAlong_NeverTakesASeat()
        {
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();
            _players.Joining = true;

            Press(dualShock.buttonSouth);
            currentTime += 0.03;
            Press(_pad.buttonSouth);
            Wait();
            Release(dualShock.buttonSouth);
            Release(_pad.buttonSouth);
            currentTime += 2;
            Press(_pad.buttonEast);
            currentTime += 0.03;
            Press(dualShock.buttonEast);
            Wait();

            Assert.That(_players.Players.Count, Is.EqualTo(1));
            Assert.That(_players.Players[0].Devices, Is.EqualTo(new InputDevice[] { dualShock }));
        }

        [Test]
        public void Joining_CopyPressingFirst_GivesTheSeatToThePad()
        {
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();
            _players.Joining = true;

            Press(_pad.buttonSouth);
            currentTime += 0.03;
            Press(dualShock.buttonSouth);
            Wait();

            Assert.That(_players.Players.Count, Is.EqualTo(1));
            Assert.That(_players.OwnerOf(dualShock), Is.Not.Null);
        }

        [Test]
        public void Joining_TwoPlayersPressingAsOne_TheOtherJoinsOnTheirNextPress()
        {
            _players.Joining = true;

            Press(_pad.buttonSouth);
            currentTime += 0.05;
            Press(_otherPad.buttonSouth);
            Wait();
            Assert.That(_players.Players.Count, Is.EqualTo(1));
            Assert.That(_players.OwnerOf(_pad), Is.Not.Null);

            Release(_otherPad.buttonSouth);
            Tap(_otherPad.buttonSouth);

            Assert.That(_players.Players.Count, Is.EqualTo(2));
        }

        [Test]
        public void Joining_OtherButtonsAtOnce_BothJoin()
        {
            _players.Joining = true;

            Press(_pad.buttonSouth);
            currentTime += 0.05;
            Press(_otherPad.buttonEast);
            Wait();

            Assert.That(_players.Players.Count, Is.EqualTo(2));
        }

        [Test]
        public void Input_PressOnAnotherPlayersPad_DoesNotFireTheAction()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            Tap(_otherPad.buttonSouth);
            InputAction first = _players.Players[0].Input.FindAction("Gameplay/Jump", true);
            InputAction second = _players.Players[1].Input.FindAction("Gameplay/Jump", true);

            Press(_otherPad.buttonSouth);

            Assert.That(first.IsPressed(), Is.False);
            Assert.That(second.IsPressed(), Is.True);
        }

        [Test]
        public void Lost_PadRemovedAndAddedBack_IsRegained()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            LocalPlayer<InputActionAsset> player = _players.Players[0];

            InputSystem.RemoveDevice(_pad);
            Assert.That(player.IsLost, Is.True);

            InputSystem.AddDevice(_pad);
            Press(_pad.buttonSouth);

            Assert.That(player.IsLost, Is.False);
            Assert.That(player.Input.FindAction("Gameplay/Jump", true).IsPressed(), Is.True);
            Assert.That(_events, Is.EqualTo(new[] { "joined P1", "lost P1", "regained P1" }));
        }

        [Test]
        public void Lost_NewPadPressedWhileNotJoining_TakesTheLostSeat()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            Tap(_otherPad.buttonSouth);
            _players.Joining = false;
            LocalPlayer<InputActionAsset> first = _players.Players[0];
            InputSystem.RemoveDevice(_pad);
            var fresh = InputSystem.AddDevice<Gamepad>();

            Tap(fresh.buttonSouth);

            Assert.That(_players.Players.Count, Is.EqualTo(2));
            Assert.That(first.IsLost, Is.False);
            Assert.That(first.Devices, Is.EqualTo(new InputDevice[] { fresh }));
            Assert.That(_players.OwnerOf(fresh), Is.SameAs(first));
        }

        [Test]
        public void Lost_MouseOnlyRemoved_StillPlays()
        {
            _players.Joining = true;
            Tap(_keyboard.enterKey);

            InputSystem.RemoveDevice(_mouse);

            Assert.That(_players.Players[0].IsLost, Is.False);
        }

        [Test]
        public void Lost_KeysPlayersMouseRemovedAndBack_IsHeardAgain()
        {
            _players.Joining = true;
            Tap(_keyboard.enterKey);
            LocalPlayer<InputActionAsset> player = _players.Players[0];

            InputSystem.RemoveDevice(_mouse);
            InputSystem.AddDevice(_mouse);

            Assert.That(player.IsLost, Is.False);
            Assert.That(player.Input.devices.Value.ToArray(), Does.Contain(_mouse));
            Assert.That(player.Input.FindAction("Gameplay/Look", true).controls.ToArray(), Does.Contain(_mouse.delta));
        }

        [Test]
        public void Lost_KeyboardGoneAndAFreeMouseClicked_StaysLostUntilAKeyboard()
        {
            _players.Joining = true;
            Tap(_keyboard.enterKey);
            LocalPlayer<InputActionAsset> player = _players.Players[0];
            var otherMouse = InputSystem.AddDevice<Mouse>();
            InputSystem.RemoveDevice(_keyboard);

            Tap(otherMouse.leftButton);
            Assert.That(player.IsLost, Is.True);
            Assert.That(player.Has(otherMouse), Is.False);
            Assert.That(_players.Players.Count, Is.EqualTo(1), "a mouse alone seats no one");

            InputSystem.AddDevice(_keyboard);
            Assert.That(player.IsLost, Is.False);
            Assert.That(_players.Players.Count, Is.EqualTo(1));
        }

        [Test]
        public void Regained_OwnPadBack_IsInHand()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            LocalPlayer<InputActionAsset> player = _players.Players[0];

            InputSystem.RemoveDevice(_pad);
            Assert.That(player.Device.Scheme, Is.EqualTo(ControlScheme.Keys));
            InputSystem.AddDevice(_pad);

            Assert.That(player.Device.Scheme, Is.EqualTo(ControlScheme.Pad));
            Assert.That(player.Device.Pad, Is.SameAs(_pad));
        }

        [Test]
        public void Joining_NewcomerPressingJustBeforeASeatedPlayer_Joins()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);

            Press(_otherPad.buttonSouth);
            currentTime += 0.1;
            Press(_pad.buttonSouth);
            Wait();

            Assert.That(_players.OwnerOf(_otherPad), Is.Not.Null);
        }

        [Test]
        public void Joining_NewcomerPressingJustAfterASeatedPlayer_JoinsOnTheNextPress()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);

            Press(_pad.buttonSouth);
            currentTime += 0.1;
            Press(_otherPad.buttonSouth);
            Wait();
            Assert.That(_players.OwnerOf(_otherPad), Is.Null);

            Release(_otherPad.buttonSouth);
            Tap(_otherPad.buttonSouth);
            Assert.That(_players.OwnerOf(_otherPad), Is.Not.Null);
        }

        [Test]
        public void Joining_DualShockTouchpadWithAnotherPadsSelect_SeatsOneAtMost()
        {
            var dualShock = InputSystem.AddDevice<DualShockGamepad>();
            _players.Joining = true;

            Press(dualShock.touchpadButton);
            currentTime += 0.03;
            Press(_pad.selectButton);
            Wait();

            Assert.That(_players.Players.Count, Is.EqualTo(1));
            Assert.That(_players.OwnerOf(dualShock), Is.Null, "a family's own control joins no one");
        }

        [Test]
        public void Block_CoversPlayersJoiningLater()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);

            _players.Block(this, true, InputScope.Of("Gameplay"));
            Tap(_otherPad.buttonSouth);

            foreach (LocalPlayer<InputActionAsset> player in _players.Players)
            {
                Assert.That(player.Input.FindActionMap("Gameplay", true).enabled, Is.False);
                Assert.That(player.Input.FindActionMap("Menu", true).enabled, Is.True);
            }

            _players.Block(this, false);
            Assert.That(_players.Players[1].Input.FindActionMap("Gameplay", true).enabled, Is.True);
        }

        [Test]
        public void Join_ExplicitDevices_SeatsExactlyThose()
        {
            LocalPlayer<InputActionAsset> player = _players.Join(_keyboard);

            Assert.That(player.Devices, Is.EqualTo(new InputDevice[] { _keyboard }));
            Assert.Throws<System.InvalidOperationException>(() => _players.Join(_keyboard));
        }

        [Test]
        public void Names_PlayerOnPad_NameTheirPadsKeys()
        {
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            Tap(_keyboard.enterKey);

            Assert.That(_players.OwnerOf(_pad).Names.Text("Gameplay/Jump"), Is.EqualTo("A"));
            Assert.That(_players.OwnerOf(_keyboard).Names.Text("Gameplay/Jump"), Is.EqualTo("SPACE"));
        }

        [Test]
        public void Names_IsDown_SeesOnlyThePlayersKeyboard()
        {
            var other = InputSystem.AddDevice<Keyboard>();
            LocalPlayer<InputActionAsset> player = _players.Join(_keyboard);
            KeyName space = player.Names.Keys("Gameplay/Jump")[0];

            Press(other.spaceKey);
            Assert.That(player.Names.IsDown(space), Is.False);

            Press(_keyboard.spaceKey);
            Assert.That(player.Names.IsDown(space), Is.True);
        }

        [Test]
        public void ListenAsync_AnotherPlayersPad_IsNotHeard()
        {
            _players.Dispose();
            _players = Create(new LocalPlayersOptions { Rebind = new RebindOptions("Gameplay/Jump") });
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            Tap(_otherPad.buttonSouth);
            ControlBindings bindings = _players.OwnerOf(_pad).Bindings;
            UniTask<RebindResult> task = bindings.ListenAsync(bindings.Find("jump.pad"), CancellationToken.None);

            Press(_otherPad.buttonNorth);
            Press(_otherPad.startButton);
            Wait(1);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending), "neither a key nor the cancel");

            Press(_pad.buttonNorth);
            Wait(1);

            Assert.That(task.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(bindings.Current(bindings.Find("jump.pad")), Is.EqualTo("<Gamepad>/buttonNorth"));
            ControlBindings others = _players.OwnerOf(_otherPad).Bindings;
            Assert.That(others.Current(others.Find("jump.pad")), Is.EqualTo("<Gamepad>/buttonSouth"));
        }

        [Test]
        public void ListenAsync_AnotherPlayersEsc_DoesNotCancelTheirOwnDoes()
        {
            _players.Dispose();
            _players = Create(new LocalPlayersOptions { Rebind = new RebindOptions("Gameplay/Move") });
            var otherKeyboard = InputSystem.AddDevice<Keyboard>();
            ControlBindings bindings = _players.Join(_keyboard).Bindings;
            _players.Join(otherKeyboard);
            UniTask<RebindResult> task = bindings.ListenAsync(bindings.Find("move.up"), CancellationToken.None);

            Press(otherKeyboard.escapeKey);
            Press(otherKeyboard.iKey);
            Wait(1);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));

            Press(_keyboard.iKey);
            Wait(1);
            Assert.That(task.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(bindings.Current(bindings.Find("move.up")), Is.EqualTo("<Keyboard>/i"));

            UniTask<RebindResult> again = bindings.ListenAsync(bindings.Find("move.down"), CancellationToken.None);
            Press(_keyboard.escapeKey);
            Assert.That(again.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Cancelled));
        }

        [Test]
        public void ListenAsync_KeyboardSlotForAPadPlayer_IsCancelledAtOnce()
        {
            _players.Dispose();
            _players = Create(new LocalPlayersOptions { Rebind = new RebindOptions("Gameplay/Jump") });
            _players.Joining = true;
            Tap(_pad.buttonSouth);
            ControlBindings bindings = _players.Players[0].Bindings;

            UniTask<RebindResult> task = bindings.ListenAsync(bindings.Find("jump"), CancellationToken.None);

            Assert.That(task.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Cancelled));
        }

        [Test]
        public void ActiveDevice_Owns_IgnoresOtherDevices()
        {
            using var device = new ActiveDevice(owns: d => d == _pad || d == _keyboard);

            Press(_otherPad.buttonSouth);
            Assert.That(device.Scheme, Is.EqualTo(ControlScheme.Keys));
            Assert.That(device.Pad, Is.SameAs(_pad), "the player's pad, not the last one connected");

            Press(_pad.buttonSouth);
            Assert.That(device.Scheme, Is.EqualTo(ControlScheme.Pad));

            Press(_mouse.leftButton);
            Assert.That(device.Scheme, Is.EqualTo(ControlScheme.Pad), "not the player's mouse");
            Assert.That(device.Mouse, Is.Null);
            Assert.That(device.Keyboard, Is.SameAs(_keyboard));
        }

        private LocalPlayers<InputActionAsset> Create(LocalPlayersOptions options)
        {
            var players = new LocalPlayers<InputActionAsset>(() => TestInput.Create(), options);
            players.Joined.Subscribe(p => _events.Add($"joined {p}"));
            players.Left.Subscribe(p => _events.Add($"left {p}"));
            players.Lost.Subscribe(p => _events.Add($"lost {p}"));
            players.Regained.Subscribe(p => _events.Add($"regained {p}"));
            return players;
        }

        // A press and release, then past the wait a pad's press has for a copy.
        private void Tap(ButtonControl button)
        {
            Press(button);
            Release(button);
            Wait();
        }

        private void Wait(double seconds = 0.3)
        {
            currentTime += seconds;
            InputSystem.Update();
        }
    }
}
