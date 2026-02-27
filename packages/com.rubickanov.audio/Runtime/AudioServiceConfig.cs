using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>
    /// ScriptableObject configuration for the audio service (mixer groups, pool size).
    /// </summary>
    [CreateAssetMenu(menuName = "Config/Audio Service")]
    public class AudioServiceConfig : ScriptableObject
    {
        [field: SerializeField] public AudioMixer Mixer { get; private set; } = default!;
        [field: SerializeField] public AudioMixerGroup MusicGroup { get; private set; } = default!;
        [field: SerializeField] public AudioMixerGroup SfxGroup { get; private set; } = default!;
        [field: SerializeField] public int MaxSfxSources { get; private set; } = 16;
        [field: SerializeField] public float MusicCrossfadeDuration { get; private set; } = 1f;
    }
}
