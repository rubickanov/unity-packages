using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>A game's list of cues: what a console lists and plays by name.</summary>
    [CreateAssetMenu(menuName = "Audio/Sound Cue Catalog")]
    public sealed class SoundCueCatalog : ScriptableObject
    {
        [SerializeField] private SoundCue[] _cues = Array.Empty<SoundCue>();

        public IReadOnlyList<SoundCue> Cues => _cues;

        /// <summary>Replaces the list (a builder); nulls are dropped.</summary>
        public void SetCues(IEnumerable<SoundCue> cues)
        {
            var list = new List<SoundCue>();
            foreach (var cue in cues)
                if (cue != null) list.Add(cue);
            _cues = list.ToArray();
        }

        /// <summary>By asset name, case-insensitive; null when none.</summary>
        public SoundCue? Find(string name)
        {
            foreach (var cue in _cues)
                if (cue != null && string.Equals(cue.name, name, StringComparison.OrdinalIgnoreCase)) return cue;
            return null;
        }
    }
}
