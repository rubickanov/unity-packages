using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// Interface for audio playback and volume control.
    /// </summary>
    public interface IAudioService
    {
        SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f);
        SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f);
        SoundHandle PlayLoop(in SoundConfig sound, float volumeScale = 1f);
        void StopSound(SoundHandle handle);
        void PlayMusic(in SoundConfig music);
        void StopMusic();
        void SetMasterVolume(float volume01);
        void SetMusicVolume(float volume01);
        void SetSFXVolume(float volume01);
        float MasterVolume { get; }
        float MusicVolume { get; }
        float SFXVolume { get; }
    }
}
