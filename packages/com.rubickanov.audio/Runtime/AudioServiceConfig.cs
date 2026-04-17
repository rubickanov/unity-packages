using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>
    /// ScriptableObject configuration for the audio service (mixer groups, pool size, parameter names).
    /// </summary>
    [CreateAssetMenu(menuName = "Config/Audio Service")]
    public class AudioServiceConfig : ScriptableObject
    {
        [field: SerializeField] public AudioMixer Mixer { get; private set; } = default!;

        [Header("Groups")]
        [field: SerializeField] public AudioMixerGroup MusicGroup { get; private set; } = default!;
        [field: SerializeField] public AudioMixerGroup SfxGroup { get; private set; } = default!;
        [field: SerializeField] public AudioMixerGroup? UIGroup { get; private set; }
        [field: SerializeField] public AudioMixerGroup? DialogGroup { get; private set; }
        [field: SerializeField] public AudioMixerGroup? AmbientGroup { get; private set; }

        [Header("Exposed Mixer Parameters")]
        [field: SerializeField] public string MasterVolumeParam { get; private set; } = "MasterVolume";
        [field: SerializeField] public string MusicVolumeParam { get; private set; } = "MusicVolume";
        [field: SerializeField] public string SfxVolumeParam { get; private set; } = "SFXVolume";
        [field: SerializeField] public string UIVolumeParam { get; private set; } = "";
        [field: SerializeField] public string DialogVolumeParam { get; private set; } = "";
        [field: SerializeField] public string AmbientVolumeParam { get; private set; } = "";

        [Header("Pool & Music")]
        [field: SerializeField] public int MaxSfxSources { get; private set; } = 16;
        [field: SerializeField] public float MusicCrossfadeDuration { get; private set; } = 1f;
    }
}
