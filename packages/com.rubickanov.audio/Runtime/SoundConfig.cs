using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    [Serializable]
    public struct SoundConfig
    {
        [SerializeField] private AudioResource _resource;
        [SerializeField, Range(0f, 0.5f)] private float _pitchVariation;
        [SerializeField, Tooltip("Mixer group this sound plays through. Empty: the service's SFX group.")]
        private AudioMixerGroup? _output;
        [SerializeField, Tooltip("When the pool is full, the sound with the lowest priority is evicted, oldest first. A sound below every playing one is dropped.")]
        private int _priority;
        [SerializeField, Min(0), Tooltip("Most copies of this sound playing at once. 0: no limit.")]
        private int _maxInstances;
        [SerializeField, Min(0f), Tooltip("Seconds that must pass between two starts of this sound (0.05 = at most once per 50 ms). 0: no limit.")]
        private float _minInterval;
        [SerializeField, Tooltip("Keeps playing while AudioListener.pause is set: menu and on-air sounds.")]
        private bool _playsOnPause;

        public AudioResource Resource => _resource;
        public float PitchVariation => _pitchVariation;
        public AudioMixerGroup? Output => _output;
        /// <summary>Higher wins a pool slot. Default 0.</summary>
        public int Priority => _priority;
        /// <summary>Most copies playing at once; 0 means no limit.</summary>
        public int MaxInstances => _maxInstances;
        /// <summary>Minimum seconds between two starts; 0 means no limit.</summary>
        public float MinInterval => _minInterval;
        /// <summary>Plays through <see cref="AudioListener.pause"/> (<see cref="AudioSource.ignoreListenerPause"/>).</summary>
        public bool PlaysOnPause => _playsOnPause;
        public bool IsValid => _resource != null;
    }
}
