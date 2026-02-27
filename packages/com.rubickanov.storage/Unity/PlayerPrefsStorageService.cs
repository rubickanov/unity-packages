using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.Storage
{
    public class PlayerPrefsStorageService : IStorageService
    {
        public float GetFloat(string key, float defaultValue = 0f)
            => PlayerPrefs.GetFloat(key, defaultValue);

        public UniTask SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }

        public int GetInt(string key, int defaultValue = 0)
            => PlayerPrefs.GetInt(key, defaultValue);

        public UniTask SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }

        public string GetString(string key, string defaultValue = "")
            => PlayerPrefs.GetString(key, defaultValue);

        public UniTask SetString(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }

        public bool HasKey(string key)
            => PlayerPrefs.HasKey(key);

        public UniTask DeleteKey(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }
    }
}
