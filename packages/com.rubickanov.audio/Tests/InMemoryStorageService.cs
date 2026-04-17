using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Rubickanov.Storage;

namespace Rubickanov.Audio.Tests
{
    internal class InMemoryStorageService : IStorageService
    {
        private readonly Dictionary<string, object> _values = new();

        public float GetFloat(string key, float defaultValue = 0f)
            => _values.TryGetValue(key, out var v) && v is float f ? f : defaultValue;

        public UniTask SetFloat(string key, float value)
        {
            _values[key] = value;
            return UniTask.CompletedTask;
        }

        public int GetInt(string key, int defaultValue = 0)
            => _values.TryGetValue(key, out var v) && v is int i ? i : defaultValue;

        public UniTask SetInt(string key, int value)
        {
            _values[key] = value;
            return UniTask.CompletedTask;
        }

        public string GetString(string key, string defaultValue = "")
            => _values.TryGetValue(key, out var v) && v is string s ? s : defaultValue;

        public UniTask SetString(string key, string value)
        {
            _values[key] = value;
            return UniTask.CompletedTask;
        }

        public bool HasKey(string key) => _values.ContainsKey(key);

        public UniTask DeleteKey(string key)
        {
            _values.Remove(key);
            return UniTask.CompletedTask;
        }

        public UniTask Clear()
        {
            _values.Clear();
            return UniTask.CompletedTask;
        }
    }
}
