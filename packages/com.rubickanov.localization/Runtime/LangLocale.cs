using System;
using System.Collections.Generic;

namespace Rubickanov.Localization
{
    /// <summary>
    /// Represents a language locale with code and display names.
    /// </summary>
    public readonly struct LangLocale : IEquatable<LangLocale>
    {
        /// <summary>
        /// Language code (e.g., "en", "ru", "de").
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Language name in English (e.g., "English", "Russian", "German").
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Language name in its native form (e.g., "English", "Русский", "Deutsch").
        /// </summary>
        public string NativeName { get; }

        public LangLocale(string code, string name, string nativeName)
        {
            Code = code ?? string.Empty;
            Name = name ?? string.Empty;
            NativeName = nativeName ?? string.Empty;
        }

        public LangLocale(string code) : this(code, GetNameForCode(code), GetNativeNameForCode(code))
        {
        }

        /// <summary>
        /// Empty locale representing no selection.
        /// </summary>
        public static LangLocale Empty => new(string.Empty, string.Empty, string.Empty);

        public bool IsEmpty => string.IsNullOrEmpty(Code);

        public bool Equals(LangLocale other) =>
            string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) =>
            obj is LangLocale other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(Code ?? string.Empty);

        public override string ToString() => $"{Name} ({Code})";

        public static bool operator ==(LangLocale left, LangLocale right) => left.Equals(right);
        public static bool operator !=(LangLocale left, LangLocale right) => !left.Equals(right);

        private static readonly Dictionary<string, (string Name, string NativeName)> LanguageNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = ("English", "English"),
                ["ru"] = ("Russian", "Русский"),
                ["de"] = ("German", "Deutsch"),
                ["fr"] = ("French", "Français"),
                ["es"] = ("Spanish", "Español"),
                ["it"] = ("Italian", "Italiano"),
                ["pt"] = ("Portuguese", "Português"),
                ["zh"] = ("Chinese", "中文"),
                ["ja"] = ("Japanese", "日本語"),
                ["ko"] = ("Korean", "한국어"),
                ["ar"] = ("Arabic", "العربية"),
                ["he"] = ("Hebrew", "עברית"),
                ["tr"] = ("Turkish", "Türkçe"),
                ["pl"] = ("Polish", "Polski"),
                ["nl"] = ("Dutch", "Nederlands"),
                ["sv"] = ("Swedish", "Svenska"),
                ["da"] = ("Danish", "Dansk"),
                ["no"] = ("Norwegian", "Norsk"),
                ["fi"] = ("Finnish", "Suomi"),
                ["cs"] = ("Czech", "Čeština"),
                ["hu"] = ("Hungarian", "Magyar"),
                ["ro"] = ("Romanian", "Română"),
                ["uk"] = ("Ukrainian", "Українська"),
                ["vi"] = ("Vietnamese", "Tiếng Việt"),
                ["th"] = ("Thai", "ไทย"),
                ["id"] = ("Indonesian", "Bahasa Indonesia"),
                ["ms"] = ("Malay", "Bahasa Melayu"),
                ["hi"] = ("Hindi", "हिन्दी"),
            };

        /// <summary>
        /// Gets the English name for a language code.
        /// </summary>
        public static string GetNameForCode(string code)
        {
            if (string.IsNullOrEmpty(code))
                return string.Empty;

            var primaryCode = code.Split('-')[0];

            return LanguageNames.TryGetValue(primaryCode, out var names)
                ? names.Name
                : code.ToUpperInvariant();
        }

        /// <summary>
        /// Gets the native name for a language code.
        /// </summary>
        public static string GetNativeNameForCode(string code)
        {
            if (string.IsNullOrEmpty(code))
                return string.Empty;

            var primaryCode = code.Split('-')[0];

            return LanguageNames.TryGetValue(primaryCode, out var names)
                ? names.NativeName
                : code.ToUpperInvariant();
        }
    }
}
