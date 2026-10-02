using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class KeyChordTests : InputTestFixture
    {
        private Keyboard _keyboard = null!;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [TestCase("A,B")]
        [TestCase("F1,F2")]
        public void TryParse_CommaJoinedKeys_IsRefused(string text)
        {
            Assert.IsFalse(KeyChord.TryParse(text, out _));
        }

        [Test]
        public void TryParse_ObsoleteIMESelected_IsRefused()
        {
            Assert.IsFalse(KeyChord.TryParse("IMESelected", out _));
        }

        [TestCase("F5")]
        [TestCase("ctrl+F5")]
        [TestCase("ctrl+shift+alt+Digit1")]
        [TestCase("shift+Backquote")]
        public void ToString_ParsedBack_IsTheSameChord(string text)
        {
            KeyChord.TryParse(text, out var chord);

            Assert.IsTrue(KeyChord.TryParse(chord.ToString(), out var again));
            Assert.AreEqual(chord, again);
            Assert.AreEqual(chord.GetHashCode(), again.GetHashCode());
        }

        [Test]
        public void WasPressedThisFrame_KeyAlone_MatchesOnlyTheChordWithoutModifiers()
        {
            Press(_keyboard.f5Key);

            Assert.IsTrue(new KeyChord(Key.F5).WasPressedThisFrame(_keyboard));
            Assert.IsFalse(new KeyChord(Key.F5, KeyModifiers.Ctrl).WasPressedThisFrame(_keyboard));
        }

        [Test]
        public void WasPressedThisFrame_WithCtrlHeld_MatchesOnlyTheCtrlChord()
        {
            Press(_keyboard.rightCtrlKey);
            Press(_keyboard.f5Key);

            Assert.IsTrue(new KeyChord(Key.F5, KeyModifiers.Ctrl).WasPressedThisFrame(_keyboard));
            Assert.IsFalse(new KeyChord(Key.F5).WasPressedThisFrame(_keyboard));
        }

        [Test]
        public void WasPressedThisFrame_KeyHeldFromAnEarlierFrame_IsFalse()
        {
            Press(_keyboard.f5Key);
            InputSystem.Update();

            Assert.IsFalse(new KeyChord(Key.F5).WasPressedThisFrame(_keyboard));
        }

        [Test]
        public void WasPressedThisFrame_KeyTheKeyboardDoesNotHave_IsFalse()
        {
            Press(_keyboard.f5Key);

            Assert.IsFalse(new KeyChord((Key)9999).WasPressedThisFrame(_keyboard));
        }
    }
}
