using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>How the service takes a variant when the call names none.</summary>
    public enum PickMode
    {
        /// <summary>Random, never the same variant twice in a row.</summary>
        RandomNoRepeat,
        /// <summary>Random, repeats allowed.</summary>
        Random,
        /// <summary>One after another, round.</summary>
        Sequence,
    }

    /// <summary>
    /// One sound: its variants and one set of settings for all of them. Played by
    /// <see cref="IAudioService.Play(SoundCue, float, int, float)"/> and the other cue calls.
    /// </summary>
    [CreateAssetMenu(menuName = "Audio/Sound Cue")]
    public sealed class SoundCue : ScriptableObject
    {
        [field: SerializeField, Tooltip("Clips, or one AudioRandomContainer the service plays as it is.")]
        public AudioResource[] Variants { get; private set; } = Array.Empty<AudioResource>();
        [field: SerializeField, Tooltip("How a variant is taken when the call names none.")]
        public PickMode Pick { get; private set; } = PickMode.RandomNoRepeat;

        [field: Header("Level")]
        [field: SerializeField, Range(0f, 1f), Tooltip("Linear; the call's volume multiplies it.")]
        public float Volume { get; private set; } = 1f;
        [field: SerializeField, Range(0f, 12f), Tooltip("Each start is quieter by a random 0..N dB, never louder than Volume.")]
        public float VolumeJitter { get; private set; }
        [field: SerializeField, Range(0f, 0.5f), Tooltip("Pitch = 1 ± random up to this share.")]
        public float PitchJitter { get; private set; }

        [field: Header("Mix")]
        [field: SerializeField, Tooltip("Mixer group. Empty: the kind's group, then the service's SFX group.")]
        public AudioMixerGroup? Output { get; private set; }
        [field: SerializeField, Tooltip("How it sounds in 3D. Empty: the service's global 3D settings. Play is always 2D.")]
        public SoundKind? Kind { get; private set; }
        [field: SerializeField, Range(0, 100), Tooltip("0..100, higher matters more: pool eviction and Unity voices (SoundPriority).")]
        public int Priority { get; private set; } = SoundPriority.Default;

        [field: Header("Limits")]
        [field: SerializeField, Min(0), Tooltip("Copies of this sound at once, over all variants. 0: no limit.")]
        public int MaxInstances { get; private set; }
        [field: SerializeField, Min(0f), Tooltip("Seconds between two starts of this sound. 0: no limit.")]
        public float MinInterval { get; private set; }
        [field: SerializeField, Tooltip("Keeps playing while AudioListener.pause is set.")]
        public bool PlaysOnPause { get; private set; }

        [field: SerializeField, TextArea(1, 4), Tooltip("When it sounds.")]
        public string Note { get; private set; } = "";

        /// <summary>The group it plays through: <see cref="Output"/>, the kind's, then <paramref name="fallback"/>.</summary>
        public AudioMixerGroup? OutputOr(AudioMixerGroup? fallback)
        {
            if (Output != null) return Output;
            if (Kind != null && Kind.Output != null) return Kind.Output;
            return fallback;
        }

        /// <summary>Makes a cue in code (tests, probes). Assets come from the Inspector or a builder.</summary>
        public static SoundCue Create(AudioResource[] variants, PickMode pick = PickMode.RandomNoRepeat, float volume = 1f,
            float volumeJitter = 0f, float pitchJitter = 0f, AudioMixerGroup? output = null, SoundKind? kind = null,
            int priority = SoundPriority.Default, int maxInstances = 0, float minInterval = 0f, bool playsOnPause = false,
            string note = "")
        {
            var cue = CreateInstance<SoundCue>();
            cue.Variants = variants ?? Array.Empty<AudioResource>();
            cue.Pick = pick;
            cue.Volume = Mathf.Clamp01(volume);
            cue.VolumeJitter = Mathf.Clamp(volumeJitter, 0f, 12f);
            cue.PitchJitter = Mathf.Clamp(pitchJitter, 0f, 0.5f);
            cue.Output = output;
            cue.Kind = kind;
            cue.Priority = Mathf.Clamp(priority, 0, 100);
            cue.MaxInstances = Mathf.Max(0, maxInstances);
            cue.MinInterval = Mathf.Max(0f, minInterval);
            cue.PlaysOnPause = playsOnPause;
            cue.Note = note ?? "";
            return cue;
        }

        /// <summary>Replaces the variants (a builder syncing clips); the settings stay.</summary>
        public void SetVariants(AudioResource[] variants) => Variants = variants ?? Array.Empty<AudioResource>();
    }
}
