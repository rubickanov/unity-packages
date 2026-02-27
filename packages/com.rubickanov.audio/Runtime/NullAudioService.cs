using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// No-op audio service for server and headless builds.
    /// </summary>
    public class NullAudioService : IAudioService
    {
        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f) => SoundHandle.Invalid;
        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f) => SoundHandle.Invalid;
        public SoundHandle PlayLoop(in SoundConfig sound, float volumeScale = 1f) => SoundHandle.Invalid;
        public void StopSound(SoundHandle handle) { }
        public void PlayMusic(in SoundConfig music) { }
        public void StopMusic() { }
        public void SetMasterVolume(float volume01) { }
        public void SetMusicVolume(float volume01) { }
        public void SetSFXVolume(float volume01) { }
        public float MasterVolume => 0f;
        public float MusicVolume => 0f;
        public float SFXVolume => 0f;
    }
}
