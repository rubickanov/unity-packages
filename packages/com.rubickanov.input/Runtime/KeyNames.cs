using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Rubickanov.Input
{
    /// <summary>
    /// The two ways to play: keyboard and mouse, or a pad. A binding belongs to one by its group (the asset's control
    /// scheme that needs a pad, or a keyboard or mouse) or, without schemes, by the device of its path.
    /// </summary>
    public enum ControlScheme
    {
        Keys,
        Pad,
    }

    /// <summary>Whose buttons a pad has, for their names and, later, their pictures.</summary>
    public enum PadFamily
    {
        Xbox,
        PlayStation,
        Nintendo,
        SteamDeck,
    }

    /// <summary>What kind of device a key belongs to.</summary>
    public enum KeyDevice
    {
        Keyboard,
        Mouse,
        Pad,
        Other,
    }

    /// <summary>
    /// One key as the player sees it: its name in English capitals, and what it is (device, control path on it, the pad's
    /// family) for when the names give way to pictures.
    /// </summary>
    public readonly struct KeyName
    {
        public readonly string Text;
        public readonly KeyDevice Device;

        /// <summary>The control's path on its device: "w", "leftButton", "buttonSouth", "leftStick/up".</summary>
        public readonly string? Control;

        public readonly PadFamily Family;

        /// <summary>The composite part it is ("up", "negative"); null for a plain binding.</summary>
        public readonly string? Part;

        public KeyName(string text, KeyDevice device, string? control, PadFamily family, string? part = null)
        {
            Text = text;
            Device = device;
            Control = control;
            Family = family;
            Part = part;
        }

        public KeyName WithPart(string? part) => new(Text, Device, Control, Family, part);

        public override string ToString() => Text;
    }

    /// <summary>
    /// What a control is called, in English whatever the player's language. A letter or sign key takes the letter the
    /// player's own layout prints on it when that is a Latin one (AZERTY's W is Z, QWERTZ's Y is Z), otherwise the US
    /// name (a Russian layout's W stays W, not Ц); digits are always their digit; the other keys, the mouse and the pads
    /// have fixed short names, the pads by family (A or CROSS for the same bottom button).
    /// </summary>
    public static class KeyNames
    {
        private static readonly Dictionary<Key, string> Short = new()
        {
            { Key.Escape, "ESC" },
            { Key.Enter, "ENTER" },
            { Key.Space, "SPACE" },
            { Key.Tab, "TAB" },
            { Key.Backspace, "BACKSPACE" },
            { Key.LeftShift, "SHIFT" },
            { Key.RightShift, "SHIFT" },
            { Key.LeftCtrl, "CTRL" },
            { Key.RightCtrl, "CTRL" },
            { Key.LeftAlt, "ALT" },
            { Key.RightAlt, "ALT" },
            { Key.CapsLock, "CAPS" },
            { Key.PageUp, "PGUP" },
            { Key.PageDown, "PGDN" },
            { Key.Home, "HOME" },
            { Key.End, "END" },
            { Key.Insert, "INS" },
            { Key.Delete, "DEL" },
            { Key.UpArrow, "UP" },
            { Key.DownArrow, "DOWN" },
            { Key.LeftArrow, "LEFT" },
            { Key.RightArrow, "RIGHT" },
            { Key.PrintScreen, "PRTSC" },
            { Key.ScrollLock, "SCRLK" },
            { Key.Pause, "PAUSE" },
            { Key.NumLock, "NUMLK" },
            { Key.ContextMenu, "MENU" },
            { Key.NumpadEnter, "NUM ENTER" },
            { Key.NumpadDivide, "NUM /" },
            { Key.NumpadMultiply, "NUM *" },
            { Key.NumpadPlus, "NUM +" },
            { Key.NumpadMinus, "NUM -" },
            { Key.NumpadPeriod, "NUM ." },
            { Key.NumpadEquals, "NUM =" },
        };

        // The US signs of the sign keys, for when the layout's own is not Latin.
        private static readonly Dictionary<Key, string> Signs = new()
        {
            { Key.Backquote, "`" },
            { Key.Quote, "'" },
            { Key.Semicolon, ";" },
            { Key.Comma, "," },
            { Key.Period, "." },
            { Key.Slash, "/" },
            { Key.Backslash, "\\" },
            { Key.LeftBracket, "[" },
            { Key.RightBracket, "]" },
            { Key.Minus, "-" },
            { Key.Equals, "=" },
        };

        private static readonly Dictionary<string, string> MouseNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "leftButton", "LMB" },
            { "rightButton", "RMB" },
            { "middleButton", "MMB" },
            { "backButton", "MB4" },
            { "forwardButton", "MB5" },
            { "scroll", "WHEEL" },
            { "scroll/y", "WHEEL" },
            { "scroll/x", "WHEEL" },
            { "scroll/up", "WHEEL UP" },
            { "scroll/down", "WHEEL DOWN" },
            { "delta", "MOUSE" },
            { "position", "MOUSE" },
        };

        // The same for every family.
        private static readonly Dictionary<string, string> PadCommon = new(StringComparer.OrdinalIgnoreCase)
        {
            { "leftStick", "LS" },
            { "rightStick", "RS" },
            { "leftStickPress", "L3" },
            { "rightStickPress", "R3" },
            { "dpad", "D-PAD" },
        };

        private static readonly Dictionary<string, string> Xbox = new(StringComparer.OrdinalIgnoreCase)
        {
            { "buttonSouth", "A" },
            { "buttonEast", "B" },
            { "buttonWest", "X" },
            { "buttonNorth", "Y" },
            { "leftShoulder", "LB" },
            { "rightShoulder", "RB" },
            { "leftTrigger", "LT" },
            { "rightTrigger", "RT" },
            { "start", "MENU" },
            { "select", "VIEW" },
        };

        private static readonly Dictionary<string, string> PlayStation = new(StringComparer.OrdinalIgnoreCase)
        {
            { "buttonSouth", "CROSS" },
            { "buttonEast", "CIRCLE" },
            { "buttonWest", "SQUARE" },
            { "buttonNorth", "TRIANGLE" },
            { "leftShoulder", "L1" },
            { "rightShoulder", "R1" },
            { "leftTrigger", "L2" },
            { "rightTrigger", "R2" },
            { "start", "OPTIONS" },
            { "select", "SHARE" },
        };

        // Nintendo's A is on the east: the button in the south is B.
        private static readonly Dictionary<string, string> Nintendo = new(StringComparer.OrdinalIgnoreCase)
        {
            { "buttonSouth", "B" },
            { "buttonEast", "A" },
            { "buttonWest", "Y" },
            { "buttonNorth", "X" },
            { "leftShoulder", "L" },
            { "rightShoulder", "R" },
            { "leftTrigger", "ZL" },
            { "rightTrigger", "ZR" },
            { "start", "+" },
            { "select", "-" },
        };

        private static readonly Dictionary<string, string> SteamDeck = new(StringComparer.OrdinalIgnoreCase)
        {
            { "buttonSouth", "A" },
            { "buttonEast", "B" },
            { "buttonWest", "X" },
            { "buttonNorth", "Y" },
            { "leftShoulder", "L1" },
            { "rightShoulder", "R1" },
            { "leftTrigger", "L2" },
            { "rightTrigger", "R2" },
            { "start", "MENU" },
            { "select", "VIEW" },
        };

        private static readonly Dictionary<string, string> Directions = new(StringComparer.OrdinalIgnoreCase)
        {
            { "up", "UP" },
            { "down", "DOWN" },
            { "left", "LEFT" },
            { "right", "RIGHT" },
        };

        /// <summary>
        /// The name of the key at <paramref name="key"/>'s place, given what the player's layout calls it
        /// (<paramref name="layoutName"/>, the key's <c>displayName</c>; null without a keyboard).
        /// </summary>
        public static string KeyText(Key key, string? layoutName)
        {
            if (key is >= Key.Digit1 and <= Key.Digit0)
            {
                return key == Key.Digit0 ? "0" : ((char)('1' + (key - Key.Digit1))).ToString();
            }

            if (key is >= Key.Numpad0 and <= Key.Numpad9)
            {
                return "NUM " + (char)('0' + (key - Key.Numpad0));
            }

            bool letter = key is >= Key.A and <= Key.Z;
            if (letter || Signs.ContainsKey(key))
            {
                if (IsLatin(layoutName))
                {
                    return layoutName!.ToUpperInvariant();
                }

                return letter ? key.ToString() : Signs[key];
            }

            return Short.TryGetValue(key, out string? name) ? name : key.ToString().ToUpperInvariant();
        }

        /// <summary>The name of a mouse control by its path on the mouse ("leftButton", "scroll/y").</summary>
        public static string MouseText(string control) =>
            MouseNames.TryGetValue(control, out string? name) ? name : Fallback(control);

        /// <summary>The name of a pad control by its path on the pad ("buttonSouth", "leftStick/up").</summary>
        public static string PadText(PadFamily family, string control)
        {
            if (FamilyNames(family).TryGetValue(control, out string? name) || PadCommon.TryGetValue(control, out name))
            {
                return name;
            }

            int slash = control.IndexOf('/');
            if (slash > 0 && Directions.TryGetValue(control[(slash + 1)..], out string? way))
            {
                return PadText(family, control[..slash]) + " " + way;
            }

            return Fallback(control);
        }

        /// <summary>Whose buttons <paramref name="pad"/> has, by its layout; a pad Unity does not know is named as an Xbox one.</summary>
        public static PadFamily FamilyOf(InputDevice? pad)
        {
            if (pad == null)
            {
                return PadFamily.Xbox;
            }

            if (InputSystem.IsFirstLayoutBasedOnSecond(pad.layout, "DualShockGamepad"))
            {
                return PadFamily.PlayStation;
            }

            return InputSystem.IsFirstLayoutBasedOnSecond(pad.layout, "SwitchProControllerHID")
                ? PadFamily.Nintendo
                : PadFamily.Xbox;
        }

        /// <summary>
        /// The key at binding path <paramref name="path"/> ("&lt;Keyboard&gt;/w"), named for the player's
        /// <paramref name="keyboard"/> (null: the US names) and a pad of <paramref name="family"/>.
        /// </summary>
        public static KeyName Name(string path, Keyboard? keyboard, PadFamily family)
        {
            string layoutText = InputControlPath.ToHumanReadableString(path, out string? layout, out string? control,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (string.IsNullOrEmpty(layout) || string.IsNullOrEmpty(control))
            {
                return new KeyName(layoutText.ToUpperInvariant(), KeyDevice.Other, control, family);
            }

            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "Keyboard"))
            {
                return new KeyName(KeyboardName(control!, keyboard), KeyDevice.Keyboard, control, family);
            }

            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "Mouse"))
            {
                return new KeyName(MouseText(control!), KeyDevice.Mouse, control, family);
            }

            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "Gamepad"))
            {
                return new KeyName(PadText(family, control!), KeyDevice.Pad, control, family);
            }

            return new KeyName(layoutText.ToUpperInvariant(), KeyDevice.Other, control, family);
        }

        /// <summary>
        /// The keys of one hint as one cap: four single letters run together (WASD), anything else is spaced (PGUP PGDN,
        /// LB RB).
        /// </summary>
        public static string Join(IReadOnlyList<KeyName> keys)
        {
            if (keys.Count == 0)
            {
                return "";
            }

            bool letters = keys.Count == 4;
            var texts = new string[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                texts[i] = keys[i].Text;
                letters &= texts[i].Length == 1;
            }

            return string.Join(letters ? "" : " ", texts);
        }

        private static string KeyboardName(string control, Keyboard? keyboard)
        {
            var key = keyboard?.TryGetChildControl<KeyControl>(control);
            if (key != null)
            {
                return KeyText(key.keyCode, key.displayName);
            }

            // The digit keys are called "1".."0", which the enum would read as numbers.
            if (control is { Length: 1 } && char.IsDigit(control[0]))
            {
                return control;
            }

            return Enum.TryParse(control, true, out Key code) ? KeyText(code, null) : Fallback(control);
        }

        // One printable character of the Latin script: ASCII signs and letters, the accented ones (Ö, É, Ñ).
        private static bool IsLatin(string? name) =>
            name is { Length: 1 } && (name[0] is > ' ' and < '\u007F' || (char.IsLetter(name[0]) && name[0] <= 'ɏ'));

        private static Dictionary<string, string> FamilyNames(PadFamily family) => family switch
        {
            PadFamily.PlayStation => PlayStation,
            PadFamily.Nintendo => Nintendo,
            PadFamily.SteamDeck => SteamDeck,
            _ => Xbox,
        };

        private static string Fallback(string control) => control.ToUpperInvariant();
    }
}
