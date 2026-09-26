using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    [Serializable]
    public struct MusicConfig
    {
        [SerializeField] private AudioResource _resource;
        [SerializeField, Tooltip("Keeps playing while AudioListener.pause is set: pause menu music.")]
        private bool _playsOnPause;

        public AudioResource Resource => _resource;
        /// <summary>Plays through <see cref="AudioListener.pause"/> (<see cref="AudioSource.ignoreListenerPause"/>).</summary>
        public bool PlaysOnPause => _playsOnPause;
        public bool IsValid => _resource != null;
    }
}
