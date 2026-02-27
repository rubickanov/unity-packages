namespace Rubickanov.Audio
{
    public interface IAudioPersistence
    {
        float Load(string key, float defaultValue);
        void Save(string key, float value);
    }
}
