using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

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
                        bool isWrite = IsFieldWritten(source, fieldVar, field);
                        bool isRead = IsFieldRead(source, fieldVar, field);
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

            string[] guids = AssetDatabase.FindAssets("t:MonoScript");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("Aspect")) continue;

                string source = File.ReadAllText(path);
                if (!source.Contains("IEntityAspect")) continue;

                var classMatch = Regex.Match(source, @"class\s+(\w+Aspect)\s*:");
                if (!classMatch.Success) continue;

                string aspectName = classMatch.Groups[1].Value;
                var fieldMatches = Regex.Matches(source,
                    @"public\s+(?:readonly\s+)?(?:ReactiveProperty<[^>]+>|Subject(?:<[^>]+>)?|\w+(?:<[^>]+>)?)\s+(\w+)\s*[;=]");

                var fields = new List<string>();
                foreach (Match m in fieldMatches)
                    fields.Add(m.Groups[1].Value);

                _aspectFieldsCache[aspectName] = fields;
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

        private static List<string> ParseRequiredAspects(string source)
        {
            var result = new List<string>();

            var requireMatches = Regex.Matches(source, @"Context\.Require<(\w+)>\(\)");
            foreach (Match m in requireMatches)
                result.Add(m.Groups[1].Value);

            var attrMatches = Regex.Matches(source,
                @"\[Aspect\]\s+(?:(?:private|protected|public|internal|readonly|static)\s+)*(\w+)\s+\w+");
            foreach (Match m in attrMatches)
                result.Add(m.Groups[1].Value);

            return result.Distinct().ToList();
        }

        private static string? FindFieldVariable(string source, string aspectName)
        {
            var requireMatch = Regex.Match(source, $@"(\w+)\s*=\s*Context\.Require<{Regex.Escape(aspectName)}>");
            if (requireMatch.Success) return requireMatch.Groups[1].Value;

            var attrMatch = Regex.Match(source,
                $@"\[Aspect\]\s+(?:(?:private|protected|public|internal|readonly|static)\s+)*{Regex.Escape(aspectName)}\s+(\w+)");
            return attrMatch.Success ? attrMatch.Groups[1].Value : null;
        }

        private static bool IsFieldWritten(string source, string fieldVar, string fieldName)
        {
            string escaped = Regex.Escape(fieldVar) + @"\." + Regex.Escape(fieldName);
            return Regex.IsMatch(source, escaped + @"\.Value\s*=")
                   || Regex.IsMatch(source, escaped + @"\.OnNext\(")
                   || Regex.IsMatch(source, escaped + @"\b\s*=[^=]");
        }

        private static bool IsFieldRead(string source, string fieldVar, string fieldName)
        {
            string escaped = Regex.Escape(fieldVar) + @"\." + Regex.Escape(fieldName);
            return Regex.IsMatch(source, escaped + @"\.Subscribe\(")
                   || Regex.IsMatch(source, escaped + @"\.Value(?!\s*=)")
                   || Regex.IsMatch(source, escaped + @"\b(?!\.Value)(?!\.OnNext)(?!\.Subscribe)(?!\s*=[^=])");
        }
    }
}