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
        SoundHandle PlaySFXAttached(in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f);
        void StopSound(SoundHandle handle, float fadeOut = 0f);
        /// <summary>Stops every pooled one-shot. Loop slots and music keep playing.</summary>
        void StopAllSFX(float fadeOut = 0f);

        /// <summary>Plays a 2D loop in the slot, replacing whatever the slot played.</summary>
        void PlayLoop(string slot, in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f);
        /// <summary>Plays a 3D loop at <paramref name="position"/> in the slot.</summary>
        void PlayLoopAtPoint(string slot, in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f);
        /// <summary>
        /// Plays a 3D loop in the slot that follows <paramref name="follow"/>. When the transform is destroyed the loop
        /// fades out over <see cref="AudioServiceConfig.LostTargetFadeOut"/> where it was last seen.
        /// </summary>
        void PlayLoopAttached(string slot, in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f);
        /// <summary>Moves a live loop's volume to <paramref name="volumeScale"/> over <paramref name="duration"/> seconds.</summary>
        void SetLoopVolume(string slot, float volumeScale, float duration = 0f);
        /// <summary>Moves a live loop's pitch to <paramref name="pitch"/> (absolute, not a multiplier) over <paramref name="duration"/> seconds.</summary>
        void SetLoopPitch(string slot, float pitch, float duration = 0f);
        void StopLoop(string slot, float fadeOut = 0f);
        bool IsLoopPlaying(string slot);

        void PlayMusic(in MusicConfig music, float? crossfadeDuration = null);
        void StopMusic();

        void TransitionToSnapshot(string snapshotName, float duration);

        /// <summary>Sets an exposed mixer volume parameter from a linear 0..1 value.</summary>
        void SetVolume(string mixerParam, float volume01);
        /// <summary>The last value set for the parameter, or 1 if it was never set.</summary>
        float GetVolume(string mixerParam);
    }
}
