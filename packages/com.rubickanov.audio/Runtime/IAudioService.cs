// The SoundConfig API stays for older users; the package itself still serves it.
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>
    /// Interface for audio playback and volume control.
    /// </summary>
    public interface IAudioService
    {
        [Obsolete("Use a SoundCue: Play, PlayAt, PlayAttached.")]
        SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f);
        [Obsolete("Use a SoundCue: Play, PlayAt, PlayAttached.")]
        SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f);
        [Obsolete("Use a SoundCue: Play, PlayAt, PlayAttached.")]
        SoundHandle PlaySFXAttached(in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f);
        void StopSound(SoundHandle handle, float fadeOut = 0f);
        /// <summary>Stops every pooled one-shot. Loop slots and music keep playing.</summary>
        void StopAllSFX(float fadeOut = 0f);

        /// <summary>Plays a 2D loop in the slot, replacing whatever the slot played.</summary>
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        void PlayLoop(string slot, in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f);
        /// <summary>Plays a 3D loop at <paramref name="position"/> in the slot.</summary>
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        void PlayLoopAtPoint(string slot, in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f);
        /// <summary>
        /// Plays a 3D loop in the slot that follows <paramref name="follow"/>. When the transform is destroyed the loop
        /// fades out over <see cref="AudioServiceConfig.LostTargetFadeOut"/> where it was last seen.
        /// </summary>
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        void PlayLoopAttached(string slot, in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f);
        /// <summary>Moves a live loop's volume to <paramref name="volumeScale"/> over <paramref name="duration"/> seconds.</summary>
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        void SetLoopVolume(string slot, float volumeScale, float duration = 0f);
        /// <summary>Moves a live loop's pitch to <paramref name="pitch"/> (absolute, not a multiplier) over <paramref name="duration"/> seconds.</summary>
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        void SetLoopPitch(string slot, float pitch, float duration = 0f);
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        void StopLoop(string slot, float fadeOut = 0f);
        [Obsolete("Use a SoundCue loop owned by a LoopHandle: Loop, LoopAt, LoopAttached.")]
        bool IsLoopPlaying(string slot);

        /// <summary>
        /// Crossfades to the track. A track already playing goes on instead of starting over; to restart it, call
        /// <see cref="StopMusic"/> first.
        /// </summary>
        void PlayMusic(in MusicConfig music, float? crossfadeDuration = null);
        void StopMusic();

        void TransitionToSnapshot(string snapshotName, float duration);

        /// <summary>Sets an exposed mixer volume parameter from a linear 0..1 value.</summary>
        void SetVolume(string mixerParam, float volume01);
        /// <summary>
        /// The last value set for the parameter; never set, the mixer's own value; 1 without a mixer or if the
        /// parameter is not exposed.
        /// </summary>
        float GetVolume(string mixerParam);

        // Cues. Every member below has a do-nothing default, so an older fake of the service still compiles.

        /// <summary>
        /// Plays the cue in 2D. <paramref name="variant"/> &lt; 0: the cue's <see cref="SoundCue.Pick"/>; ≥ 0:
        /// variant % its count, the same on every machine. The source's volume is cue.Volume × jitter × volume, at most 1.
        /// </summary>
        SoundHandle Play(SoundCue? cue, float volume = 1f, int variant = -1, float fadeIn = 0f) => SoundHandle.Invalid;
        /// <summary>Plays the cue in 3D at the point, through its kind when it has one.</summary>
        SoundHandle PlayAt(SoundCue? cue, Vector3 point, float volume = 1f, int variant = -1, float fadeIn = 0f) =>
            SoundHandle.Invalid;
        /// <summary>Plays the cue in 3D following the transform.</summary>
        SoundHandle PlayAttached(SoundCue? cue, Transform follow, float volume = 1f, int variant = -1, float fadeIn = 0f) =>
            SoundHandle.Invalid;

        /// <summary>Loops one variant of the cue in 2D, picked at the start; the handle owns it.</summary>
        LoopHandle Loop(SoundCue? cue, float volume = 1f, float fadeIn = 0f) => LoopHandle.Invalid;
        LoopHandle LoopAt(SoundCue? cue, Vector3 point, float volume = 1f, float fadeIn = 0f) => LoopHandle.Invalid;
        /// <summary>A destroyed transform fades the loop out where it was last seen.</summary>
        LoopHandle LoopAttached(SoundCue? cue, Transform follow, float volume = 1f, float fadeIn = 0f) => LoopHandle.Invalid;
        /// <summary>A scale over cue.Volume (and the start's jitter), at most 1 in all.</summary>
        void SetVolume(LoopHandle loop, float volume, float duration = 0f) { }
        /// <summary>Absolute pitch, as <see cref="SetLoopPitch"/>.</summary>
        void SetPitch(LoopHandle loop, float pitch, float duration = 0f) { }
        void Stop(LoopHandle loop, float fadeOut = 0f) { }
        bool IsPlaying(LoopHandle loop) => false;

        /// <summary>
        /// Puts an own source into the mix: group, kind (through the space when there is one), Unity priority from
        /// the cue scale, reverb bypass, listener pause. The handle is the space's, for its volume; invalid without a kind.
        /// </summary>
        SpatialHandle Adopt(AudioSource source, in SourceSetup setup) => SpatialHandle.Invalid;
        /// <summary>Undoes <see cref="Adopt"/>; a source destroyed meanwhile is forgotten too.</summary>
        void Release(AudioSource source) { }
        /// <summary>Whether the source is in the mix through <see cref="Adopt"/>.</summary>
        bool IsAdopted(AudioSource source) => false;

        /// <summary>The group the cue plays through.</summary>
        AudioMixerGroup? GroupOf(SoundCue cue) => null;
        /// <summary>Starts of the cue heard here.</summary>
        int Played(SoundCue cue) => 0;
        /// <summary>Adds every cue this service has seen.</summary>
        void Describe(List<CueStats> into) { }
        /// <summary>Logs every cue start, "sound &lt;cue&gt; &lt;who|at (x, y, z)|flat&gt; &lt;n&gt;", channel Audio. Off by default.</summary>
        bool LogPlays
        {
            get => false;
            set { }
        }
    }
}
