using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// Interface for audio playback and volume control.
    /// </summary>
    public interface IAudioService
    {
        SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f);
        SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f);
        SoundHandle PlaySFXAttached(in SoundConfig sound, Transform parent, float volumeScale = 1f, float fadeIn = 0f);
        void StopSound(SoundHandle handle, float fadeOut = 0f);

        void PlayLoop(string slot, in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f);
        void StopLoop(string slot, float fadeOut = 0f);
        bool IsLoopPlaying(string slot);

        void PlayMusic(in MusicConfig music, float? crossfadeDuration = null);
        void StopMusic();

        void DuckSFX(float amount01, float duration, float attack = 0.05f, float release = 0.3f);
        void TransitionToSnapshot(string snapshotName, float duration);

        void SetMasterVolume(float volume01);
        void SetMusicVolume(float volume01);
        void SetSFXVolume(float volume01);
        float MasterVolume { get; }
        float MusicVolume { get; }
        float SFXVolume { get; }
    }
}
