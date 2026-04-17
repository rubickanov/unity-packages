using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Storage
{
    public sealed class PrefixedStorageService : IStorageService
    {
        private readonly IStorageService _inner;
        private readonly string _prefix;

        public PrefixedStorageService(IStorageService inner, string prefix)
        {
            if (inner is null) throw new ArgumentNullException(nameof(inner));
            if (string.IsNullOrEmpty(prefix))
                throw new ArgumentException("Prefix must be non-empty.", nameof(prefix));

            _inner = inner;
            _prefix = prefix;
        }

        public float GetFloat(string key, float defaultValue = 0f)
            => _inner.GetFloat(_prefix + key, defaultValue);

        public UniTask SetFloat(string key, float value)
            => _inner.SetFloat(_prefix + key, value);

        public int GetInt(string key, int defaultValue = 0)
            => _inner.GetInt(_prefix + key, defaultValue);

        public UniTask SetInt(string key, int value)
            => _inner.SetInt(_prefix + key, value);

        public string GetString(string key, string defaultValue = "")
            => _inner.GetString(_prefix + key, defaultValue);

        public UniTask SetString(string key, string value)
            => _inner.SetString(_prefix + key, value);

        public bool HasKey(string key) => _inner.HasKey(_prefix + key);

        public UniTask DeleteKey(string key) => _inner.DeleteKey(_prefix + key);

        public UniTask Clear()
            => throw new NotSupportedException(
                "Clear() on a prefixed storage requires key enumeration, which IStorageService does not expose. Clear the inner storage instead, or delete known keys individually.");
    }
}
