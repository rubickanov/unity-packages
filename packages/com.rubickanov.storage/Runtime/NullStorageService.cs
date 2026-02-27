using Cysharp.Threading.Tasks;

namespace Rubickanov.Storage
{
    public class NullStorageService : IStorageService
    {
        public float GetFloat(string key, float defaultValue = 0f)
            => defaultValue;

        public UniTask SetFloat(string key, float value)
            => UniTask.CompletedTask;

        public int GetInt(string key, int defaultValue = 0)
            => defaultValue;

        public UniTask SetInt(string key, int value)
            => UniTask.CompletedTask;

        public string GetString(string key, string defaultValue = "")
            => defaultValue;

        public UniTask SetString(string key, string value)
            => UniTask.CompletedTask;

        public bool HasKey(string key)
            => false;

        public UniTask DeleteKey(string key)
            => UniTask.CompletedTask;
    }
}
