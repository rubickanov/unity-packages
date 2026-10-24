using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>How the level falls with distance.</summary>
    public enum DistanceLaw
    {
        /// <summary>Full level within Reference, −6 dB per doubling past it, faded to silence at Max.</summary>
        Inverse,
        /// <summary>Flat: the owner sets the level itself (voices with their own range).</summary>
        External,
    }

    /// <summary>
    /// What a kind of 3D sound does with distance and the listener: level, air, width, outdoor reverb, head shadow
    /// and occlusion. Applied by <see cref="SoundSpace"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Config/Sound Kind")]
    public sealed class SoundKind : ScriptableObject
    {
        [field: Header("Distance")]
        [field: SerializeField] public DistanceLaw Law { get; private set; } = DistanceLaw.Inverse;
        [field: SerializeField, Min(0.1f), Tooltip("Full level within this distance, m; −6 dB per doubling past it.")]
        public float Reference { get; private set; } = 2f;
        [field: SerializeField, Min(1f), Tooltip("Silent here, m; the curves' domain.")]
        public float Max { get; private set; } = 60f;
        [field: SerializeField, Range(0f, 0.5f), Tooltip("Last share of Max over which the level fades to 0 (smoothstep).")]
        public float FadeShare { get; private set; } = 0.2f;

        [field: Header("Air")]
        [field: SerializeField, Min(0f), Tooltip("Air absorption ×ISO 9613-1 (20 °C, 70 %): 0 off, 1 real, 3–6 game.")]
        public float Air { get; private set; } = 4f;

        [field: Header("Width")]
        [field: SerializeField, Range(0f, 180f), Tooltip("Spread up to Reference, degrees. ≤ 110 keeps a side source ≥ 6 dB louder in its ear.")]
        public float SpreadNear { get; private set; } = 110f;
        [field: SerializeField, Range(0f, 180f), Tooltip("Spread from SpreadFarAt on, degrees.")]
        public float SpreadFar { get; private set; } = 90f;
        [field: SerializeField, Min(0f), Tooltip("Spread reaches SpreadFar at this distance, m.")]
        public float SpreadFarAt { get; private set; } = 6f;

        [field: Header("Outdoor reverb")]
        [field: SerializeField, Range(0f, 1.1f), Tooltip("Reverb-zone send at Reference; rises with distance (constant diffuse field). Keep ≤ 0.1: the zone's wet sums after the mixer's limiter.")]
        public float ReverbAtReference { get; private set; } = 0.05f;
        [field: SerializeField, Range(0f, 1.1f)] public float ReverbMax { get; private set; } = 1.1f;

        [field: Header("Listener")]
        [field: SerializeField, Range(0f, 1f), Tooltip("Behind the head: highs cut and a little quieter.")]
        public float HeadShadow { get; private set; } = 1f;
        [field: SerializeField, Tooltip("A ray from the listener muffles the sound behind static world.")]
        public bool Occlusion { get; private set; }
        [field: SerializeField, Tooltip("Static world only: terrain, rocks, buildings. Never characters, vehicles, items, water.")]
        public LayerMask OcclusionMask { get; private set; } = 1;
        [field: SerializeField, Range(0, 256)] public int Priority { get; private set; } = 128;
        [field: SerializeField, Tooltip("Optional default mixer group for sounds of this kind.")]
        public AudioMixerGroup? Output { get; private set; }

        /// <summary>Needs a per-frame step: head shadow or occlusion.</summary>
        public bool Ticks => HeadShadow > 0f || Occlusion;

        [NonSerialized] internal KindCurves? Curves;

        /// <summary>Makes a kind in code (tests, probes). Assets come from the Inspector.</summary>
        public static SoundKind Create(DistanceLaw law = DistanceLaw.Inverse, float reference = 2f, float max = 60f,
            float fadeShare = 0.2f, float air = 4f, float spreadNear = 110f, float spreadFar = 90f, float spreadFarAt = 6f,
            float reverbAtReference = 0.05f, float reverbMax = 1.1f, float headShadow = 1f, bool occlusion = false,
            int occlusionMask = 1, int priority = 128, AudioMixerGroup? output = null)
        {
            var kind = CreateInstance<SoundKind>();
            kind.Law = law;
            kind.Reference = Mathf.Max(0.1f, reference);
            kind.Max = Mathf.Max(1f, max);
            kind.FadeShare = Mathf.Clamp(fadeShare, 0f, 0.5f);
            kind.Air = Mathf.Max(0f, air);
            kind.SpreadNear = Mathf.Clamp(spreadNear, 0f, 180f);
            kind.SpreadFar = Mathf.Clamp(spreadFar, 0f, 180f);
            kind.SpreadFarAt = Mathf.Max(0f, spreadFarAt);
            kind.ReverbAtReference = Mathf.Clamp(reverbAtReference, 0f, 1.1f);
            kind.ReverbMax = Mathf.Clamp(reverbMax, 0f, 1.1f);
            kind.HeadShadow = Mathf.Clamp01(headShadow);
            kind.Occlusion = occlusion;
            kind.OcclusionMask = occlusionMask;
            kind.Priority = Mathf.Clamp(priority, 0, 256);
            kind.Output = output;
            return kind;
        }

        // Inspector edits rebuild the curves on the next play.
        private void OnValidate() => Curves = null;
    }
}
