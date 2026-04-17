using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Rubickanov.Audio
{
    [Serializable]
    public struct MusicConfig
    {
        [SerializeField] private AudioResource _resource;

        public AudioResource Resource => _resource;
        public bool IsValid => _resource != null;
    }
}
