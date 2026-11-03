using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    /// <summary>How an own AudioSource (voice, speaker, engine) joins the mix: <see cref="IAudioService.Adopt"/>.</summary>
    public struct SourceSetup
    {
        /// <summary>Null: the kind's group, then the service's SFX group.</summary>
        public AudioMixerGroup? Output;
        /// <summary>Null: 2D or the source's own 3D settings, no reverb send.</summary>
        public SoundKind? Kind;
        /// <summary>The cue scale 0..100 (<see cref="SoundPriority"/>).</summary>
        public int Priority;
        /// <summary>The space's volume when a kind is given.</summary>
        public float Volume;
        /// <summary>ExternalGain for streamed voices.</summary>
        public SpatialOptions Spatial;
        public bool PlaysOnPause;

        public SourceSetup(SoundKind? kind, int priority = SoundPriority.Default, float volume = 1f,
            AudioMixerGroup? output = null)
        {
            Output = output;
            Kind = kind;
            Priority = priority;
            Volume = volume;
            Spatial = default;
            PlaysOnPause = false;
        }
    }
}
