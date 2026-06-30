using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Rubickanov.Codegen.Editor
{
    /// <summary>
    /// Turns arbitrary input strings (localization keys, tag segments, asset names, ...) into
    /// valid, unique C# identifiers. Shared by every generator so sanitization rules live in one
    /// place instead of being copy-pasted per package.
    /// </summary>
    public static class IdentifierSanitizer
    {
        private static readonly Regex InvalidChars = new(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);

        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
            "char", "checked", "class", "const", "continue", "decimal", "default",
            "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "goto", "if", "implicit", "in", "int", "interface", "internal",
            "is", "lock", "long", "namespace", "new", "null", "object", "operator",
            "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
            "ushort", "using", "virtual", "void", "volatile", "while"
        };

        /// <summary>
        /// Produces a valid C# identifier: invalid characters become underscores, a leading digit
        /// is prefixed with an underscore, the result is PascalCased, and C# keywords are escaped
        /// with <c>@</c>.
        /// </summary>
        /// <param name="lowercaseRemainder">
        /// When true, every character after a part's first is lowercased ("DoT" -> "Dot"). When
        /// false, interior casing is preserved ("DoT" -> "DoT"). This is the single behavioural
        /// difference between the localization generator (true) and the gameplay tags generator
        /// (false); keeping it a flag lets both emit byte-identical output from one routine.
        /// </param>
        public static string Sanitize(string input, bool lowercaseRemainder)
        {
            if (string.IsNullOrEmpty(input))
                return "_";

            var sanitized = InvalidChars.Replace(input, "_");
            sanitized = ToPascalCase(sanitized, lowercaseRemainder);

            // Guard the leading digit after PascalCasing: doing it before would prepend an
            // underscore that the subsequent Split('_', RemoveEmptyEntries) discards, leaving an
            // identifier that still starts with a digit and won't compile.
            if (char.IsDigit(sanitized[0]))
                sanitized = "_" + sanitized;

            if (IsCSharpKeyword(sanitized))
                sanitized = "@" + sanitized;

            return sanitized;
        }

        /// <summary>
        /// Returns <paramref name="name"/> if unused in <paramref name="used"/>, otherwise the
        /// first free <c>name_2</c>, <c>name_3</c>, ... variant. A leading <c>@</c> keyword escape
        /// is preserved on the suffixed form ("@class" -> "@class_2"). The returned name is added
        /// to <paramref name="used"/>.
        /// </summary>
        public static string MakeUnique(string name, ISet<string> used)
        {
            if (used.Add(name))
                return name;

            var hasEscape = name.StartsWith("@", StringComparison.Ordinal);
            var bare = hasEscape ? name.Substring(1) : name;
            var prefix = hasEscape ? "@" : string.Empty;

            var n = 2;
            string candidate;
            do { candidate = $"{prefix}{bare}_{n++}"; } while (!used.Add(candidate));
            return candidate;
        }

        private static string ToPascalCase(string input, bool lowercaseRemainder)
        {
            var parts = input.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();

            foreach (var part in parts)
            {
                if (part.Length == 0)
                    continue;

                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    var remainder = part.Substring(1);
                    sb.Append(lowercaseRemainder ? remainder.ToLowerInvariant() : remainder);
                }
            }

            return sb.Length > 0 ? sb.ToString() : "_";
        }

        private static bool IsCSharpKeyword(string word) => CSharpKeywords.Contains(word.ToLowerInvariant());
    }
}
