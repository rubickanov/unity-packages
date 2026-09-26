using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// No-op audio service for server and headless builds. Volume getters return last set value.
    /// </summary>
    public class NullAudioService : IAudioService
    {
        private readonly Dictionary<string, float> _volumes = new();

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        public SoundHandle PlaySFXAttached(in SoundConfig sound, Transform parent, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        public void StopSound(SoundHandle handle, float fadeOut = 0f) { }
        public void StopAllSFX(float fadeOut = 0f) { }

        public void PlayLoop(string slot, in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f) { }
        public void SetLoopVolume(string slot, float volumeScale, float duration = 0f) { }
        public void SetLoopPitch(string slot, float pitch, float duration = 0f) { }
        public void StopLoop(string slot, float fadeOut = 0f) { }
        public bool IsLoopPlaying(string slot) => false;

        public void PlayMusic(in MusicConfig music, float? crossfadeDuration = null) { }
        public void StopMusic() { }

        public void TransitionToSnapshot(string snapshotName, float duration) { }

        public void SetVolume(string mixerParam, float volume01)
        {
            if (!string.IsNullOrEmpty(mixerParam)) _volumes[mixerParam] = Mathf.Clamp01(volume01);
        }

        public float GetVolume(string mixerParam) =>
            !string.IsNullOrEmpty(mixerParam) && _volumes.TryGetValue(mixerParam, out var volume) ? volume : 1f;
    }
}
