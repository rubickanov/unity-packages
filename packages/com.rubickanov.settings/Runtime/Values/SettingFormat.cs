using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rubickanov.Settings
{
    /// <summary>How a value goes to the file and to the console. A game adds its own for its own types.</summary>
    public abstract class SettingFormat<T>
    {
        public abstract SettingsJsonValue Write(T value);

        /// <summary>False when the kept value is not one of this type (the store warns and keeps the default).</summary>
        public abstract bool TryRead(SettingsJsonValue value, out T result);

        /// <summary>The value as the console shows it.</summary>
        public abstract string Show(T value);

        /// <summary>The console's text as a value.</summary>
        public abstract bool TryParse(string text, out T result);
    }

    /// <summary>The formats of the common types.</summary>
    public static class SettingFormats
    {
        public static SettingFormat<int> Int { get; } = new IntFormat();

        /// <summary>Invariant culture both ways.</summary>
        public static SettingFormat<float> Float { get; } = new FloatFormat();

        /// <summary>true / false; the console also takes on / off, yes / no, 1 / 0.</summary>
        public static SettingFormat<bool> Bool { get; } = new BoolFormat();

        public static SettingFormat<string> String { get; } = new StringFormat();

        /// <summary><c>"1920x1080"</c>, <c>"native"</c>.</summary>
        public static SettingFormat<ScreenSize> ScreenSize { get; } = new ScreenSizeFormat();

        /// <summary>The name in lower case (<c>"fullscreen"</c>); reads any case, never a number.</summary>
        public static SettingFormat<T> Enum<T>() where T : struct, System.Enum => EnumFormat<T>.Instance;

        private sealed class IntFormat : SettingFormat<int>
        {
            public override SettingsJsonValue Write(int value) => SettingsJsonValue.Of(value);

            public override bool TryRead(SettingsJsonValue value, out int result)
            {
                result = 0;
                if (value.Kind != SettingsJsonKind.Number || double.IsNaN(value.Number) ||
                    value.Number < int.MinValue || value.Number > int.MaxValue) return false;
                result = (int)Math.Round(value.Number);
                return true;
            }

            public override string Show(int value) => value.ToString(CultureInfo.InvariantCulture);

            public override bool TryParse(string text, out int result) =>
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private sealed class FloatFormat : SettingFormat<float>
        {
            public override SettingsJsonValue Write(float value) => SettingsJsonValue.Of(value);

            public override bool TryRead(SettingsJsonValue value, out float result)
            {
                result = 0f;
                if (value.Kind != SettingsJsonKind.Number || double.IsNaN(value.Number)) return false;
                result = (float)value.Number;
                return true;
            }

            public override string Show(float value) => value.ToString(CultureInfo.InvariantCulture);

            public override bool TryParse(string text, out float result) =>
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private sealed class BoolFormat : SettingFormat<bool>
        {
            public override SettingsJsonValue Write(bool value) => SettingsJsonValue.Of(value);

            public override bool TryRead(SettingsJsonValue value, out bool result)
            {
                result = value.Bool;
                return value.Kind == SettingsJsonKind.Bool;
            }

            public override string Show(bool value) => value ? "true" : "false";

            public override bool TryParse(string text, out bool result)
            {
                switch (text.ToLowerInvariant())
                {
                    case "true":
                    case "on":
                    case "yes":
                    case "1":
                        result = true;
                        return true;
                    case "false":
                    case "off":
                    case "no":
                    case "0":
                        result = false;
                        return true;
                    default:
                        result = false;
                        return false;
                }
            }
        }

        private sealed class StringFormat : SettingFormat<string>
        {
            public override SettingsJsonValue Write(string value) => SettingsJsonValue.Of(value ?? "");

            public override bool TryRead(SettingsJsonValue value, out string result)
            {
                result = value.String ?? "";
                return value.Kind == SettingsJsonKind.String;
            }

            public override string Show(string value) => value ?? "";

            public override bool TryParse(string text, out string result)
            {
                result = text;
                return true;
            }
        }

        private sealed class ScreenSizeFormat : SettingFormat<ScreenSize>
        {
            public override SettingsJsonValue Write(ScreenSize value) => SettingsJsonValue.Of(value.ToString());

            public override bool TryRead(SettingsJsonValue value, out ScreenSize result)
            {
                result = Settings.ScreenSize.Native;
                return value.Kind == SettingsJsonKind.String && Settings.ScreenSize.TryParse(value.String!, out result);
            }

            public override string Show(ScreenSize value) => value.ToString();

            public override bool TryParse(string text, out ScreenSize result) => Settings.ScreenSize.TryParse(text, out result);
        }

        private sealed class EnumFormat<T> : SettingFormat<T> where T : struct, System.Enum
        {
            public static readonly EnumFormat<T> Instance = new();

            private readonly Dictionary<T, string> _names = new();
            private readonly Dictionary<string, T> _values = new(StringComparer.OrdinalIgnoreCase);

            private EnumFormat()
            {
                foreach (T value in (T[])System.Enum.GetValues(typeof(T)))
                {
                    string name = value.ToString().ToLowerInvariant();
                    _names.TryAdd(value, name);
                    _values.TryAdd(name, value);
                }
            }

            public override SettingsJsonValue Write(T value) => SettingsJsonValue.Of(Show(value));

            public override bool TryRead(SettingsJsonValue value, out T result)
            {
                result = default;
                return value.Kind == SettingsJsonKind.String && TryParse(value.String!, out result);
            }

            public override string Show(T value) => _names.TryGetValue(value, out string name) ? name : value.ToString();

            public override bool TryParse(string text, out T result) => _values.TryGetValue(text.Trim(), out result);
        }
    }
}
