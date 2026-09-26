using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>
    /// ScriptableObject configuration for the audio service (mixer, default groups, pool size, 3D and time settings).
    /// </summary>
    [CreateAssetMenu(menuName = "Config/Audio Service")]
    public class AudioServiceConfig : ScriptableObject
    {
        [field: SerializeField] public AudioMixer Mixer { get; private set; } = default!;

        [Header("Groups")]
        [field: SerializeField] public AudioMixerGroup MusicGroup { get; private set; } = default!;
        [field: SerializeField, Tooltip("Where a sound with no output of its own plays.")]
        public AudioMixerGroup SfxGroup { get; private set; } = default!;

        [Header("Pool & Music")]
        [field: SerializeField] public int MaxSfxSources { get; private set; } = 16;
        [field: SerializeField] public float MusicCrossfadeDuration { get; private set; } = 1f;

        [Header("3D")]
        [field: SerializeField] public AudioRolloffMode Rolloff { get; private set; } = AudioRolloffMode.Logarithmic;
        [field: SerializeField, Min(0f)] public float MinDistance { get; private set; } = 1f;
        [field: SerializeField, Min(0f)] public float MaxDistance { get; private set; } = 500f;
        [field: SerializeField, Range(0f, 5f)] public float DopplerLevel { get; private set; } = 1f;
        [field: SerializeField, Min(0f), Tooltip("Seconds an attached loop fades out for when the object it follows is destroyed.")]
        public float LostTargetFadeOut { get; private set; } = 0.25f;

        [Header("Time")]
        [field: SerializeField, Tooltip("Fades, crossfades and ramps run on real time, so slow motion and a paused time scale don't stretch or freeze them.")]
        public bool UnscaledTime { get; private set; } = true;
    }
}
