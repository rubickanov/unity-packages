using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Switch;
using UnityEngine.InputSystem.XInput;

namespace Rubickanov.Input.Tests
{
    public sealed class KeyNamesTests : InputTestFixture
    {
        private Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [Test]
        public void Name_LatinLayout_TakesTheLetterPrintedOnTheKey()
        {
            SetKeyInfo(Key.W, "z");
            SetKeyInfo(Key.Semicolon, "ö");

            Assert.That(Name("<Keyboard>/w"), Is.EqualTo("Z"), "AZERTY's W");
            Assert.That(Name("<Keyboard>/semicolon"), Is.EqualTo("Ö"), "QWERTZ's semicolon");
        }

        [Test]
        public void Name_NonLatinLayout_KeepsTheUSName()
        {
            SetKeyInfo(Key.W, "Ц");
            SetKeyInfo(Key.F, "А");
            SetKeyInfo(Key.Semicolon, "Ж");

            Assert.That(Name("<Keyboard>/w"), Is.EqualTo("W"));
            Assert.That(Name("<Keyboard>/f"), Is.EqualTo("F"), "a Russian layout's F is not А");
            Assert.That(Name("<Keyboard>/semicolon"), Is.EqualTo(";"));
        }

        [Test]
        public void Name_DigitKeys_StayDigitsOnAnyLayout()
        {
            SetKeyInfo(Key.Digit1, "&");
            SetKeyInfo(Key.Digit0, "à");

            Assert.That(Name("<Keyboard>/1"), Is.EqualTo("1"), "AZERTY's top row");
            Assert.That(Name("<Keyboard>/0"), Is.EqualTo("0"));
            Assert.That(Name("<Keyboard>/numpad7"), Is.EqualTo("NUM 7"));
        }

        [Test]
        public void Name_OtherKeys_ShortEnglishNames()
        {
            SetKeyInfo(Key.Enter, "Eingabe");

            Assert.That(Name("<Keyboard>/enter"), Is.EqualTo("ENTER"), "not the layout's own word");
            Assert.That(Name("<Keyboard>/leftShift"), Is.EqualTo("SHIFT"));
            Assert.That(Name("<Keyboard>/escape"), Is.EqualTo("ESC"));
            Assert.That(Name("<Keyboard>/pageUp"), Is.EqualTo("PGUP"));
            Assert.That(Name("<Keyboard>/upArrow"), Is.EqualTo("UP"));
            Assert.That(Name("<Keyboard>/f5"), Is.EqualTo("F5"));
        }

        [Test]
        public void Name_NoKeyboard_USNames()
        {
            Assert.That(KeyNames.Name("<Keyboard>/w", null, PadFamily.Xbox).Text, Is.EqualTo("W"));
            Assert.That(KeyNames.Name("<Keyboard>/space", null, PadFamily.Xbox).Text, Is.EqualTo("SPACE"));
        }

        [Test]
        public void Name_Mouse_ByButtonsAndMovement()
        {
            Assert.That(Name("<Mouse>/leftButton"), Is.EqualTo("LMB"));
            Assert.That(Name("<Mouse>/delta"), Is.EqualTo("MOUSE"));
            Assert.That(Name("<Mouse>/scroll/y"), Is.EqualTo("WHEEL"));
        }

        [Test]
        public void Name_Key_CarriesDeviceControlAndFamily()
        {
            KeyName key = KeyNames.Name("<Gamepad>/buttonSouth", null, PadFamily.PlayStation);

            Assert.That(key.Device, Is.EqualTo(KeyDevice.Pad));
            Assert.That(key.Control, Is.EqualTo("buttonSouth"));
            Assert.That(key.Family, Is.EqualTo(PadFamily.PlayStation));
            Assert.That(KeyNames.Name("<Keyboard>/w", null, PadFamily.Xbox).Device, Is.EqualTo(KeyDevice.Keyboard));
            Assert.That(KeyNames.Name("<Mouse>/leftButton", null, PadFamily.Xbox).Device, Is.EqualTo(KeyDevice.Mouse));
        }

        [Test]
        public void FamilyOf_Layout_NamesThePadsMaker()
        {
            Assert.That(KeyNames.FamilyOf(InputSystem.AddDevice<Gamepad>()), Is.EqualTo(PadFamily.Xbox));
            Assert.That(KeyNames.FamilyOf(InputSystem.AddDevice<XInputController>()), Is.EqualTo(PadFamily.Xbox));
            Assert.That(KeyNames.FamilyOf(InputSystem.AddDevice<DualShock4GamepadHID>()), Is.EqualTo(PadFamily.PlayStation));
            Assert.That(KeyNames.FamilyOf(InputSystem.AddDevice<DualSenseGamepadHID>()), Is.EqualTo(PadFamily.PlayStation));
            Assert.That(KeyNames.FamilyOf(InputSystem.AddDevice<SwitchProControllerHID>()), Is.EqualTo(PadFamily.Nintendo));
            Assert.That(KeyNames.FamilyOf(null), Is.EqualTo(PadFamily.Xbox));
        }

        [Test]
        public void PadText_EachFamily_NamesItsButtons()
        {
            Assert.That(Pad(PadFamily.Xbox, "buttonSouth"), Is.EqualTo("A"));
            Assert.That(Pad(PadFamily.Xbox, "leftShoulder"), Is.EqualTo("LB"));
            Assert.That(Pad(PadFamily.PlayStation, "buttonSouth"), Is.EqualTo("CROSS"));
            Assert.That(Pad(PadFamily.PlayStation, "rightTrigger"), Is.EqualTo("R2"));
            Assert.That(Pad(PadFamily.Nintendo, "buttonSouth"), Is.EqualTo("B"), "Nintendo's A is on the east");
            Assert.That(Pad(PadFamily.Nintendo, "buttonEast"), Is.EqualTo("A"));
            Assert.That(Pad(PadFamily.SteamDeck, "leftShoulder"), Is.EqualTo("L1"));
            Assert.That(Pad(PadFamily.Xbox, "leftStick"), Is.EqualTo("LS"));
            Assert.That(Pad(PadFamily.Xbox, "leftStick/up"), Is.EqualTo("LS UP"));
            Assert.That(Pad(PadFamily.PlayStation, "dpad"), Is.EqualTo("D-PAD"));
        }

        [Test]
        public void Join_FourLetters_RunTogetherAndTheRestIsSpaced()
        {
            Assert.That(KeyNames.Join(Keys("W", "A", "S", "D")), Is.EqualTo("WASD"));
            Assert.That(KeyNames.Join(Keys("PGUP", "PGDN")), Is.EqualTo("PGUP PGDN"));
            Assert.That(KeyNames.Join(Keys("Q", "E")), Is.EqualTo("Q E"));
            Assert.That(KeyNames.Join(Keys("UP", "LEFT", "DOWN", "RIGHT")), Is.EqualTo("UP LEFT DOWN RIGHT"));
            Assert.That(KeyNames.Join(Keys()), Is.EqualTo(""));
        }

        private string Name(string path) => KeyNames.Name(path, _keyboard, PadFamily.Xbox).Text;

        private static string Pad(PadFamily family, string control) =>
            KeyNames.Name("<Gamepad>/" + control, null, family).Text;

        private static KeyName[] Keys(params string[] texts)
        {
            var keys = new KeyName[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                keys[i] = new KeyName(texts[i], KeyDevice.Keyboard, texts[i].ToLowerInvariant(), PadFamily.Xbox);
            }

            return keys;
        }
    }
}
