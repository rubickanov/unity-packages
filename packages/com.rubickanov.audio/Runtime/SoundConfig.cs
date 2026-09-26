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

        public AudioResource Resource => _resource;
        public float PitchVariation => _pitchVariation;
        public AudioMixerGroup? Output => _output;
        public bool IsValid => _resource != null;
    }
}
