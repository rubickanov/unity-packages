using System;
using System.Text;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole
{
    /// <summary>Modifier keys a <see cref="KeyChord"/> needs held. Left and right count the same.</summary>
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Ctrl = 1,
        Shift = 2,
        Alt = 4
    }

    /// <summary>A key with the modifiers that must be held with it, written <c>ctrl+shift+F5</c>.</summary>
    public readonly struct KeyChord : IEquatable<KeyChord>
    {
        public readonly Key Key;
        public readonly KeyModifiers Modifiers;

        public KeyChord(Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            Key = key;
            Modifiers = modifiers;
        }

        /// <summary>
        /// Parses <c>F5</c>, <c>ctrl+F5</c>, <c>shift+alt+K</c>: modifiers first, then an InputSystem <see cref="Key"/> name,
        /// case-insensitive. A bare number is refused rather than read as an enum value.
        /// </summary>
        public static bool TryParse(string text, out KeyChord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+');
            var modifiers = KeyModifiers.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= KeyModifiers.Ctrl;
                        break;
                    case "shift":
                        modifiers |= KeyModifiers.Shift;
                        break;
                    case "alt":
                    case "option":
                        modifiers |= KeyModifiers.Alt;
                        break;
                    default:
                        return false;
                }
            }

            var keyName = parts[^1].Trim();
            if (keyName.Length == 0 || char.IsDigit(keyName[0]) || keyName[0] == '-') return false;
            if (!Enum.TryParse(keyName, true, out Key key) || key == Key.None) return false;

            chord = new KeyChord(key, modifiers);
            return true;
        }

        /// <summary>Whether the key went down this frame with exactly these modifiers held.</summary>
        public bool WasPressedThisFrame(Keyboard keyboard)
        {
            return keyboard[Key].wasPressedThisFrame && HeldModifiers(keyboard) == Modifiers;
        }

        private static KeyModifiers HeldModifiers(Keyboard keyboard)
        {
            var held = KeyModifiers.None;
            if (keyboard.ctrlKey.isPressed) held |= KeyModifiers.Ctrl;
            if (keyboard.shiftKey.isPressed) held |= KeyModifiers.Shift;
            if (keyboard.altKey.isPressed) held |= KeyModifiers.Alt;
            return held;
        }

        public override string ToString()
        {
            if (Modifiers == KeyModifiers.None) return Key.ToString();

            var sb = new StringBuilder();
            if ((Modifiers & KeyModifiers.Ctrl) != 0) sb.Append("ctrl+");
            if ((Modifiers & KeyModifiers.Shift) != 0) sb.Append("shift+");
            if ((Modifiers & KeyModifiers.Alt) != 0) sb.Append("alt+");
            return sb.Append(Key).ToString();
        }

        public bool Equals(KeyChord other) => Key == other.Key && Modifiers == other.Modifiers;
        public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);
        public override int GetHashCode() => ((int)Key << 3) | (int)Modifiers;
    }
}
