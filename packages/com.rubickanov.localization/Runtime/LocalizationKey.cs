using System;

namespace Rubickanov.Localization
{
    /// <summary>
    /// Strongly-typed key for localized strings combining table reference and entry key.
    /// </summary>
    public readonly struct LocalizationKey : IEquatable<LocalizationKey>
    {
        /// <summary>
        /// String table name (e.g., "UI", "Items", "Dialogs").
        /// </summary>
        public string Table { get; }

        /// <summary>
        /// Entry key within the table.
        /// </summary>
        public string Key { get; }

        public LocalizationKey(string table, string key)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public bool Equals(LocalizationKey other) =>
            Table == other.Table && Key == other.Key;

        public override bool Equals(object? obj) =>
            obj is LocalizationKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Table, Key);

        public override string ToString() => $"{Table}/{Key}";

        public static bool operator ==(LocalizationKey left, LocalizationKey right) =>
            left.Equals(right);

        public static bool operator !=(LocalizationKey left, LocalizationKey right) =>
            !left.Equals(right);
    }
}
