using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Rubickanov.Storage
{
    public sealed class FileStorageService : IStorageService
    {
        private readonly string _filePath;
        private readonly ILogger<FileStorageService>? _logger;
        private readonly Dictionary<string, string> _data = new();

        private Task _pendingSave = Task.CompletedTask;

        public FileStorageService(string filePath, ILogger<FileStorageService>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path must be non-empty.", nameof(filePath));

            _filePath = filePath;
            _logger = logger;

            if (!File.Exists(filePath)) return;

            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                Deserialize(json);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                var bak = $"{filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                try { File.Move(filePath, bak); }
                catch { /* best effort: if move fails, the next Save will overwrite the corrupt file */ }

                _logger?.LogWarning(
                    "Corrupted storage at {Path}, backed up to {Backup}: {Message}",
                    filePath, bak, ex.Message);
                _data.Clear();
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
            // "R" (round-trip) guarantees the parsed value equals the original; the default
            // format can drop low-order bits and silently corrupt persisted floats.
            _data[key] = value.ToString("R", CultureInfo.InvariantCulture);
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

        public UniTask Clear()
        {
            _data.Clear();
            return SaveAsync();
        }

        private UniTask SaveAsync()
        {
            var json = Serialize();
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _pendingSave = ChainSave(_pendingSave, _filePath, json, _logger);
            return AwaitTask(_pendingSave);
        }

        private static async UniTask AwaitTask(Task task)
        {
            await task;
        }

        private static async Task ChainSave(
            Task previous,
            string filePath,
            string json,
            ILogger<FileStorageService>? logger)
        {
            try { await previous.ConfigureAwait(false); } catch { /* previous failure already logged */ }

            try
            {
                // Write to a sibling temp file then swap it in, so a crash mid-write truncates
                // the throwaway temp instead of the live file. File.Replace is atomic where the
                // platform supports it; Move covers the first-ever save (no file to replace).
                var tmpPath = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8).ConfigureAwait(false);

                if (File.Exists(filePath))
                    File.Replace(tmpPath, filePath, null);
                else
                    File.Move(tmpPath, filePath);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to save storage to {Path}", filePath);
                throw;
            }
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
                AppendEscaped(sb, kvp.Key);
                sb.Append("\":\"");
                AppendEscaped(sb, kvp.Value);
                sb.Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private void Deserialize(string json)
        {
            _data.Clear();
            var originalLength = json.Length;
            var span = json.AsSpan().Trim();
            if (span.Length < 2 || span[0] != '{' || span[^1] != '}')
                throw new InvalidDataException($"Expected JSON object at position {originalLength - span.Length}.");

            span = span[1..^1].Trim();
            while (span.Length > 0)
            {
                var key = ReadJsonString(ref span, originalLength);

                span = span.TrimStart();
                if (span.Length == 0 || span[0] != ':')
                    throw new InvalidDataException($"Expected ':' at position {originalLength - span.Length}.");
                span = span[1..].TrimStart();

                var value = ReadJsonString(ref span, originalLength);

                _data[key] = value;

                span = span.TrimStart();
                if (span.Length == 0) break;
                if (span[0] != ',')
                    throw new InvalidDataException($"Expected ',' or '}}' at position {originalLength - span.Length}.");
                span = span[1..].TrimStart();
            }
        }

        private static string ReadJsonString(ref ReadOnlySpan<char> span, int originalLength)
        {
            if (span.Length == 0 || span[0] != '"')
                throw new InvalidDataException($"Expected '\"' at position {originalLength - span.Length}.");
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
            throw new InvalidDataException($"Unterminated string at position {originalLength - span.Length}.");
        }

        private static void AppendEscaped(StringBuilder sb, string s)
        {
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:   sb.Append(c); break;
                }
            }
        }
    }
}
