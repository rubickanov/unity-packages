using Cysharp.Threading.Tasks;

namespace Rubickanov.Storage
{
    public interface IStorageService
    {
        float GetFloat(string key, float defaultValue = 0f);
        UniTask SetFloat(string key, float value);
        int GetInt(string key, int defaultValue = 0);
        UniTask SetInt(string key, int value);
        string GetString(string key, string defaultValue = "");
        UniTask SetString(string key, string value);
        bool HasKey(string key);
        UniTask DeleteKey(string key);
    }
}
