using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Rubickanov.ACS.Runtime;
using UnityEditor;

[assembly: InternalsVisibleTo("ACS.Tests")]

namespace Rubickanov.ACS.Editor
{
    public readonly struct FieldBinding
    {
        public readonly string ComponentName;
        public readonly bool IsRead;
        public readonly bool IsWrite;

        public FieldBinding(string componentName, bool isRead, bool isWrite)
        {
            ComponentName = componentName;
            IsRead = isRead;
            IsWrite = isWrite;
        }
    }

    public readonly struct AspectFieldInfo
    {
        public readonly string FieldName;
        public readonly List<FieldBinding> Bindings;

        public AspectFieldInfo(string fieldName, List<FieldBinding> bindings)
        {
            FieldName = fieldName;
            Bindings = bindings;
        }
    }

    public readonly struct AspectInfo
    {
        public readonly string AspectName;
        public readonly List<AspectFieldInfo> Fields;

        public AspectInfo(string aspectName, List<AspectFieldInfo> fields)
        {
            AspectName = aspectName;
            Fields = fields;
        }
    }

    public static class AspectUsageAnalyzer
    {
        private static readonly Dictionary<string, List<string>> _aspectFieldsCache = new();
        private static bool _aspectFieldsLoaded;

        public static List<AspectInfo> AnalyzeEntity(IEnumerable<Type> componentTypes)
        {
            EnsureAspectFields();

            // aspect -> field -> list of bindings
            var map = new Dictionary<string, Dictionary<string, List<FieldBinding>>>();

            foreach (var type in componentTypes)
            {
                var source = FindAndReadSource(type);
                if (source == null) continue;

                var aspects = ParseRequiredAspects(source);

                foreach (string aspectName in aspects)
                {
                    var fieldVar = FindFieldVariable(source, aspectName);
                    if (fieldVar == null) continue;

                    if (!_aspectFieldsCache.TryGetValue(aspectName, out var aspectFields)) continue;

                    if (!map.ContainsKey(aspectName))
                        map[aspectName] = new Dictionary<string, List<FieldBinding>>();

                    foreach (string field in aspectFields)
                    {
                        AnalyzeFieldUsage(source, fieldVar, field, out bool isRead, out bool isWrite);
                        if (!isRead && !isWrite) continue;

                        if (!map[aspectName].ContainsKey(field))
                            map[aspectName][field] = new List<FieldBinding>();

                        map[aspectName][field].Add(new FieldBinding(type.Name, isRead, isWrite));
                    }
                }
            }

            var result = new List<AspectInfo>();
            foreach (var (aspectName, fields) in map.OrderBy(kv => kv.Key))
            {
                if (fields.Count == 0) continue;

                var fieldInfos = new List<AspectFieldInfo>();
                foreach (var (fieldName, bindings) in fields.OrderBy(kv => kv.Key))
                    fieldInfos.Add(new AspectFieldInfo(fieldName, bindings));

                result.Add(new AspectInfo(aspectName, fieldInfos));
            }

            return result;
        }

        [InitializeOnLoadMethod]
        private static void ClearCache()
        {
            _aspectFieldsCache.Clear();
            _aspectFieldsLoaded = false;
        }

        private static void EnsureAspectFields()
        {
            if (_aspectFieldsLoaded) return;
            _aspectFieldsLoaded = true;

            var aspectTypes = TypeCache.GetTypesDerivedFrom<IEntityAspect>();
            foreach (var type in aspectTypes)
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                var fields = new List<string>();
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    fields.Add(field.Name);

                _aspectFieldsCache[type.Name] = fields;
            }
        }

        private static string? FindAndReadSource(Type type)
        {
            string[] guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == type.Name)
                    return File.ReadAllText(path);
            }
            return null;
        }

        internal static List<string> ParseRequiredAspects(string source)
        {
            var result = new List<string>();

            // Any receiver chain: `Context.Require<X>()`, `World.Require<X>()`, `entity.Require<X>()`,
            // `_ctx.foo.Require<X>()`. Static-import `Require<X>()` is out of scope.
            var requireMatches = Regex.Matches(source, @"\w+(?:\.\w+)*\.Require<(\w+)>\(\)");
            foreach (Match m in requireMatches)
                result.Add(m.Groups[1].Value);

            var attrMatches = Regex.Matches(source,
                @"\[Aspect\]\s+(?:(?:private|protected|public|internal|readonly|static)\s+)*(\w+)\s+\w+");
            foreach (Match m in attrMatches)
                result.Add(m.Groups[1].Value);

            return result.Distinct().ToList();
        }

        internal static string? FindFieldVariable(string source, string aspectName)
        {
            var requireMatch = Regex.Match(source,
                $@"(\w+)\s*=\s*\w+(?:\.\w+)*\.Require<{Regex.Escape(aspectName)}>");
            if (requireMatch.Success) return requireMatch.Groups[1].Value;

            var attrMatch = Regex.Match(source,
                $@"\[Aspect\]\s+(?:(?:private|protected|public|internal|readonly|static)\s+)*{Regex.Escape(aspectName)}\s+(\w+)");
            return attrMatch.Success ? attrMatch.Groups[1].Value : null;
        }

        // Scans `source` for uses of `fieldVar.fieldName` and decides read/write in one pass.
        // Replaces the prior regex-per-field approach that thrashed the static Regex cache.
        internal static void AnalyzeFieldUsage(
            string source, string fieldVar, string fieldName,
            out bool isRead, out bool isWrite)
        {
            isRead = false;
            isWrite = false;

            string needle = fieldVar + "." + fieldName;
            int i = 0;
            while ((i = source.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                int end = i + needle.Length;

                // leading word boundary — `fieldVar` must not be a suffix of a longer identifier
                if (i > 0 && IsWordChar(source[i - 1])) { i = end; continue; }

                if (StartsWith(source, end, ".Value"))
                {
                    int afterValue = end + ".Value".Length;
                    if (afterValue < source.Length && IsWordChar(source[afterValue]))
                        isRead = true; // e.g. `.ValueType` — generic member access
                    else if (IsAssignmentAt(source, afterValue))
                        isWrite = true;
                    else
                        isRead = true;
                }
                else if (StartsWith(source, end, ".OnNext("))
                    isWrite = true;
                else if (StartsWith(source, end, ".Subscribe("))
                    isRead = true;
                else
                {
                    // trailing word boundary — fieldName must not be a prefix of a longer identifier
                    if (end < source.Length && IsWordChar(source[end])) { i = end; continue; }

                    if (IsAssignmentAt(source, end))
                        isWrite = true;
                    else
                        isRead = true;
                }

                if (isRead && isWrite) return;
                i = end;
            }
        }

        // True if `source[pos..]` is an assignment: optional whitespace, optional
        // compound operator (+ - * /), then `=` not followed by another `=` (i.e. not `==`).
        private static bool IsAssignmentAt(string source, int pos)
        {
            int p = pos;
            while (p < source.Length && (source[p] == ' ' || source[p] == '\t')) p++;
            if (p < source.Length && (source[p] == '+' || source[p] == '-'
                                      || source[p] == '*' || source[p] == '/'))
                p++;
            if (p >= source.Length || source[p] != '=') return false;
            return p + 1 >= source.Length || source[p + 1] != '=';
        }

        private static bool StartsWith(string source, int pos, string s)
            => pos + s.Length <= source.Length
               && string.CompareOrdinal(source, pos, s, 0, s.Length) == 0;

        private static bool IsWordChar(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
               || (c >= '0' && c <= '9') || c == '_';
    }
}