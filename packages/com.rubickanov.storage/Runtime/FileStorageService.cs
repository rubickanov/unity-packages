using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Storage
{
    public class FileStorageService : IStorageService
    {
        private readonly string _filePath;
        private readonly Dictionary<string, string> _data = new();

        public FileStorageService(string filePath)
        {
            _filePath = filePath;

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                Deserialize(json);
            }
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (_data.TryGetValue(key, out var raw) &&
                float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            return defaultValue;
        }

        public UniTask SetFloat(string key, float value)
        {
            _data[key] = value.ToString(CultureInfo.InvariantCulture);
            return SaveAsync();
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (_data.TryGetValue(key, out var raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;

            return defaultValue;
        }

        public UniTask SetInt(string key, int value)
        {
            _data[key] = value.ToString(CultureInfo.InvariantCulture);
            return SaveAsync();
        }

        public string GetString(string key, string defaultValue = "")
        {
            return _data.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public UniTask SetString(string key, string value)
        {
            _data[key] = value;
            return SaveAsync();
        }

        public bool HasKey(string key)
        {
            return _data.ContainsKey(key);
        }

        public UniTask DeleteKey(string key)
        {
            _data.Remove(key);
            return SaveAsync();
        }

        private async UniTask SaveAsync()
        {
            var json = Serialize();
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await UniTask.SwitchToThreadPool();
            await File.WriteAllTextAsync(_filePath, json, Encoding.UTF8);
            await UniTask.SwitchToMainThread();
        }

        private string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (var kvp in _data)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"');
                sb.Append(EscapeJson(kvp.Key));
                sb.Append("\":\"");
                sb.Append(EscapeJson(kvp.Value));
                sb.Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private void Deserialize(string json)
        {
            _data.Clear();
            var span = json.AsSpan().Trim();
            if (span.Length < 2 || span[0] != '{' || span[^1] != '}') return;

            span = span[1..^1].Trim();
            while (span.Length > 0)
            {
                var key = ReadJsonString(ref span);
                if (key == null) break;

                span = span.TrimStart();
                if (span.Length == 0 || span[0] != ':') break;
                span = span[1..].TrimStart();

                var value = ReadJsonString(ref span);
                if (value == null) break;

                _data[key] = value;

                span = span.TrimStart();
                if (span.Length > 0 && span[0] == ',')
                    span = span[1..].TrimStart();
            }
        }

        private static string? ReadJsonString(ref ReadOnlySpan<char> span)
        {
            if (span.Length == 0 || span[0] != '"') return null;
            span = span[1..];

            var sb = new StringBuilder();
            while (span.Length > 0)
            {
                if (span[0] == '\\' && span.Length > 1)
                {
                    sb.Append(span[1] switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => span[1]
                    });
                    span = span[2..];
                }
                else if (span[0] == '"')
                {
                    span = span[1..];
                    return sb.ToString();
                }
                else
                {
                    sb.Append(span[0]);
                    span = span[1..];
                }
            }
            return null;
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
