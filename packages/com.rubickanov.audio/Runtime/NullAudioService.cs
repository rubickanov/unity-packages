// The SoundConfig API stays for older users; the package itself still serves it.
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>
    /// No-op audio service for server and headless builds. Volume getters return last set value.
    /// </summary>
    public class NullAudioService : IAudioService
    {
        private readonly Dictionary<string, float> _volumes = new();

        [Obsolete("Use a SoundCue: Play, PlayAt, PlayAttached.")]

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        [Obsolete("Use a SoundCue: Play, PlayAt, PlayAttached.")]
        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        [Obsolete("Use a SoundCue: Play, PlayAt, PlayAttached.")]
        public SoundHandle PlaySFXAttached(in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f) => SoundHandle.Invalid;
        public void StopSound(SoundHandle handle, float fadeOut = 0f) { }
        public void StopAllSFX(float fadeOut = 0f) { }

        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]

        public void PlayLoop(string slot, in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f) { }
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        public void PlayLoopAtPoint(string slot, in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f) { }
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        public void PlayLoopAttached(string slot, in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f) { }
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        public void SetLoopVolume(string slot, float volumeScale, float duration = 0f) { }
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        public void SetLoopPitch(string slot, float pitch, float duration = 0f) { }
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        public void StopLoop(string slot, float fadeOut = 0f) { }
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
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

        public SoundHandle Play(SoundCue? cue, float volume = 1f, int variant = -1, float fadeIn = 0f) => SoundHandle.Invalid;
        public SoundHandle PlayAt(SoundCue? cue, Vector3 point, float volume = 1f, int variant = -1, float fadeIn = 0f) => SoundHandle.Invalid;
        public SoundHandle PlayAttached(SoundCue? cue, Transform follow, float volume = 1f, int variant = -1, float fadeIn = 0f) => SoundHandle.Invalid;

        public LoopHandle Loop(SoundCue? cue, float volume = 1f, float fadeIn = 0f) => LoopHandle.Invalid;
        public LoopHandle LoopAt(SoundCue? cue, Vector3 point, float volume = 1f, float fadeIn = 0f) => LoopHandle.Invalid;
        public LoopHandle LoopAttached(SoundCue? cue, Transform follow, float volume = 1f, float fadeIn = 0f) => LoopHandle.Invalid;
        public void SetVolume(LoopHandle loop, float volume, float duration = 0f) { }
        public void SetPitch(LoopHandle loop, float pitch, float duration = 0f) { }
        public void Stop(LoopHandle loop, float fadeOut = 0f) { }
        public bool IsPlaying(LoopHandle loop) => false;

        public SpatialHandle Adopt(AudioSource source, in SourceSetup setup) => SpatialHandle.Invalid;
        public void Release(AudioSource source) { }

        public AudioMixerGroup? GroupOf(SoundCue cue) => cue != null ? cue.OutputOr(null) : null;
        public int Played(SoundCue cue) => 0;
        public void Describe(List<CueStats> into) { }
        public bool LogPlays { get; set; }
    }
}
