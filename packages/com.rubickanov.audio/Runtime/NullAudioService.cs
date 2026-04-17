using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// No-op audio service for server and headless builds. Volume getters return last set value.
    /// </summary>
    public class NullAudioService : IAudioService
    {
        private float _masterVolume = 1f;
        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        public SoundHandle PlaySFXAttached(in SoundConfig sound, Transform parent, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        public void StopSound(SoundHandle handle, float fadeOut = 0f) { }

        public void PlayLoop(string slot, in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f) { }
        public void StopLoop(string slot, float fadeOut = 0f) { }
        public bool IsLoopPlaying(string slot) => false;

        public void PlayMusic(in MusicConfig music, float? crossfadeDuration = null) { }
        public void StopMusic() { }

        public void DuckSFX(float amount01, float duration, float attack = 0.05f, float release = 0.3f) { }
        public void TransitionToSnapshot(string snapshotName, float duration) { }

        public void SetMasterVolume(float volume01) => _masterVolume = volume01;
        public void SetMusicVolume(float volume01) => _musicVolume = volume01;
        public void SetSFXVolume(float volume01) => _sfxVolume = volume01;
        public float MasterVolume => _masterVolume;
        public float MusicVolume => _musicVolume;
        public float SFXVolume => _sfxVolume;
    }
}
