using System.Globalization;

namespace Rubickanov.Settings
{
    public enum SettingsJsonKind
    {
        Null,
        Number,
        Bool,
        String,

        /// <summary>JSON text the reader does not split into ids: an array, an empty object.</summary>
        Raw,
    }

    /// <summary>One value of the settings file: a scalar, or raw JSON text for what is kept without being understood.</summary>
    public readonly struct SettingsJsonValue
    {
        private SettingsJsonValue(SettingsJsonKind kind, double number, bool @bool, string? text)
        {
            Kind = kind;
            Number = number;
            Bool = @bool;
            String = kind == SettingsJsonKind.String ? text : null;
            Raw = kind == SettingsJsonKind.Raw ? text : null;
        }

        public static SettingsJsonValue Null => default;

        public SettingsJsonKind Kind { get; }

        public double Number { get; }

        public bool Bool { get; }

        public string? String { get; }

        public string? Raw { get; }

        public static SettingsJsonValue Of(double number) => new(SettingsJsonKind.Number, number, false, null);

        public static SettingsJsonValue Of(bool value) => new(SettingsJsonKind.Bool, 0, value, null);

        public static SettingsJsonValue Of(string text) => new(SettingsJsonKind.String, 0, false, text ?? "");

        /// <summary>JSON text kept as it is (an array, an object).</summary>
        public static SettingsJsonValue OfRaw(string json) => new(SettingsJsonKind.Raw, 0, false, json);

        public override string ToString() => Kind switch
        {
            SettingsJsonKind.Number => Number.ToString(CultureInfo.InvariantCulture),
            SettingsJsonKind.Bool => Bool ? "true" : "false",
            SettingsJsonKind.String => String!,
            SettingsJsonKind.Raw => Raw!,
            _ => "null",
        };
    }
}
