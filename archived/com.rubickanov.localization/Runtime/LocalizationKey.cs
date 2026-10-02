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
            if (string.IsNullOrWhiteSpace(table))
                throw new ArgumentException("Table must be a non-empty string.", nameof(table));
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key must be a non-empty string.", nameof(key));

            Table = table;
            Key = key;
        }

        /// <summary>
        /// True when both <see cref="Table"/> and <see cref="Key"/> are non-empty.
        /// False for <c>default(LocalizationKey)</c>.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Table) && !string.IsNullOrEmpty(Key);

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
