using System.Linq;
using NUnit.Framework;
using R3;
using Rubickanov.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Rubickanov.Settings.Input.Tests
{
    public sealed class ControlKeysTests : InputTestFixture
    {
        private Keyboard _keyboard = null!;
        private Gamepad _pad = null!;
        private InputActionAsset _input = null!;
        private InputBlocker _blocker = null!;
        private ActiveDevice _device = null!;
        private ControlBindings _bindings = null!;
        private ControlNames _names = null!;
        private ControlKeys _keys = null!;
        private SettingsMenu _menu = null!;
        private float _now;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _input = CreateInput();
            _blocker = new InputBlocker(_input);
            _device = new ActiveDevice();
            _bindings = new ControlBindings(_input, _blocker,
                new RebindOptions("Gameplay/Move", "Gameplay/Jump", "Gameplay/Interact"));
            _names = new ControlNames(_input, _device);
            _keys = new ControlKeys(_bindings, _names, _device, "Menu/Submit");
            _keys.Changed.Subscribe(_ => _menu?.Rebuild());
            var text = SettingsText.English(_ => { }).With(new SettingsText()
                .Row("keys.Gameplay/Move", "MOVE")
                .Row("keys.Gameplay/Jump", "JUMP")
                .Row("keys.Gameplay/Interact", "INTERACT"));
            _menu = new SettingsMenu(new SettingsLayout().Section("controls", s =>
            {
                _keys.AddRows(s);
                s.ResetRow();
            }), text, () => _now);
        }

        public override void TearDown()
        {
            _menu.Dispose();
            _keys.Dispose();
            _names.Dispose();
            _bindings.Dispose();
            _device.Dispose();
            _blocker.Dispose();
            _input.Disable();
            Object.DestroyImmediate(_input);
            base.TearDown();
        }

        [Test]
        public void AKeyIsListenedForAndAFreeOneTakesItsPlace()
        {
            Assert.That(Labels(), Is.EqualTo(new[] { "KEYS", "MOVE", "JUMP", "INTERACT", "RESET SECTION" }));
            Assert.That(((KeysRow)_menu.Rows[1]).KeyText(1), Is.EqualTo("A"), "ways read W A S D");
            Assert.That(_menu.Focused!.Id, Is.EqualTo("keys.Gameplay/Move"), "the heading is stepped over");

            _menu.Step(SettingsStep.Right);
            _menu.Step(SettingsStep.Right);
            _menu.Submit();
            Assert.That(_menu.Listening, Is.EqualTo("keys.Gameplay/Move"));
            Assert.That(_menu.Description, Is.EqualTo("PRESS A KEY FOR MOVE. ESC CANCELS"));
            _menu.Step(SettingsStep.Down);
            Assert.That(_menu.Focused!.Id, Is.EqualTo("keys.Gameplay/Move"), "nothing moves while a key is awaited");

            Press(_keyboard.kKey);
            Settle();

            Assert.That(_menu.Listening, Is.Null);
            Assert.That(_bindings.Current(_bindings.Find("move.down")!), Is.EqualTo("<Keyboard>/k"));
            Assert.That(((KeysRow)_menu.Focused!).KeyText(2), Is.EqualTo("K"));
            Assert.That(((KeysRow)_menu.Focused!).IsDefault(2), Is.False);
            Assert.That(((KeysRow)_menu.Focused!).KeyFocus, Is.EqualTo(2), "the picked key stays after the rows are made again");
        }

        [Test]
        public void ATakenKeyIsSwappedOnlyWhenThePlayerAgrees()
        {
            _menu.Step(SettingsStep.Down);
            Assert.That(_menu.Focused!.Id, Is.EqualTo("keys.Gameplay/Jump"));

            ListenAndPress(_keyboard.eKey);
            Assert.That(_menu.QuestionText, Is.EqualTo("E IS ON INTERACT. SWAP?"));
            Assert.That(Current("jump"), Is.EqualTo("<Keyboard>/space"), "nothing changes before the answer");
            _menu.Step(SettingsStep.Right);
            _menu.Submit();
            Assert.That(_menu.Question, Is.Null);
            Assert.That(Current("jump"), Is.EqualTo("<Keyboard>/space"), "no leaves both");

            ListenAndPress(_keyboard.eKey);
            _menu.Submit();
            Assert.That(Current("jump"), Is.EqualTo("<Keyboard>/e"));
            Assert.That(Current("interact"), Is.EqualTo("<Keyboard>/space"));
        }

        [Test]
        public void EscBacksOutAndAKeyTheSlotNeverTakesIsDenied()
        {
            var cues = new System.Collections.Generic.List<SettingsCue>();
            _menu.Cues.Subscribe(cues.Add);
            _menu.Step(SettingsStep.Down);

            ListenAndPress(_keyboard.escapeKey);
            Assert.That(_menu.Listening, Is.Null);
            Assert.That(cues.Last(), Is.EqualTo(SettingsCue.Back));
            Assert.That(Current("jump"), Is.EqualTo("<Keyboard>/space"));
        }

        [Test]
        public void ThePadHasItsOwnButtons()
        {
            Press(_pad.buttonNorth);
            Release(_pad.buttonNorth);
            Settle();

            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad));
            Assert.That(Labels(), Is.EqualTo(new[] { "BUTTONS", "JUMP", "INTERACT", "RESET SECTION" }),
                "made again for the pad; a stick bound whole is no button");
            Assert.That(((KeysRow)_menu.Focused!).KeyText(0), Is.EqualTo("A"));

            _menu.Submit();
            Assert.That(_menu.Description, Is.EqualTo("PRESS A BUTTON FOR JUMP. START CANCELS"));
            Press(_pad.startButton);
            Settle();
            Assert.That(_menu.Listening, Is.Null);
            Assert.That(Current("jump.pad"), Is.EqualTo("<Gamepad>/buttonSouth"));
        }

        [Test]
        public void TheSectionResetPutsBackOnlyTheSchemeInHand()
        {
            _bindings.Set(_bindings.Find("jump")!, "<Keyboard>/f");
            _bindings.Set(_bindings.Find("jump.pad")!, "<Gamepad>/buttonNorth");

            for (int i = 0; i < 4; i++) _menu.Step(SettingsStep.Down);
            Assert.That(_menu.Focused!.Id, Is.EqualTo(SectionBuilder.ResetId));
            _menu.Submit();
            Assert.That(_menu.Question!.Choice, Is.EqualTo(1), "asks with NO first");
            _menu.Answer(true);

            Assert.That(Current("jump"), Is.EqualTo("<Keyboard>/space"));
            Assert.That(Current("jump.pad"), Is.EqualTo("<Gamepad>/buttonNorth"), "the pad's keep theirs");
        }

        [Test]
        public void TheDeviceSwitchingWhileListeningWaitsForTheKey()
        {
            _menu.Step(SettingsStep.Down);
            _menu.Submit();
            object row = _menu.Focused!;

            Press(_pad.buttonNorth);
            Settle();
            Assert.That(_device.Scheme, Is.EqualTo(ControlScheme.Pad));
            Assert.That(_menu.Focused, Is.SameAs(row), "the listened row stays");

            Press(_keyboard.gKey);
            Settle();
            Assert.That(Current("jump"), Is.EqualTo("<Keyboard>/g"));
            Assert.That(_menu.Listening, Is.Null);
        }

        private void ListenAndPress(KeyControl key)
        {
            _menu.Submit();
            Assert.That(_menu.Listening, Is.Not.Null);
            Press(key);
            Release(key);
            Settle();
        }

        private string Current(string slot) => _bindings.Current(_bindings.Find(slot)!);

        private string[] Labels() => _menu.Rows.Select(row => _menu.Text.Get(row.Id)).ToArray();

        private void Settle()
        {
            currentTime += 1.0;
            InputSystem.Update();
        }

        private static InputActionAsset CreateInput()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap gameplay = asset.AddActionMap("Gameplay");
            InputAction move = gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", "Keys")
                .With("Down", "<Keyboard>/s", "Keys")
                .With("Left", "<Keyboard>/a", "Keys")
                .With("Right", "<Keyboard>/d", "Keys");
            move.AddBinding("<Gamepad>/leftStick", groups: "Pad");
            Button(gameplay, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Button(gameplay, "Interact", "<Keyboard>/e", "<Gamepad>/buttonWest");
            InputActionMap menu = asset.AddActionMap("Menu");
            Button(menu, "Submit", "<Keyboard>/enter", "<Gamepad>/buttonSouth");
            Button(menu, "Cancel", "<Keyboard>/escape", "<Gamepad>/buttonEast");
            asset.AddControlScheme("Keys").WithRequiredDevice("<Keyboard>").WithRequiredDevice("<Mouse>");
            asset.AddControlScheme("Pad").WithRequiredDevice("<Gamepad>");
            asset.Enable();
            return asset;
        }

        private static void Button(InputActionMap map, string name, string key, string button)
        {
            InputAction action = map.AddAction(name, InputActionType.Button);
            action.AddBinding(key, groups: "Keys");
            action.AddBinding(button, groups: "Pad");
        }
    }
}
