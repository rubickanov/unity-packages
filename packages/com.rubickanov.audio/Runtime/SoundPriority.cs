using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// One priority scale, 0..100, higher matters more: for cues, own sources and the pool. A sound of the old
    /// <see cref="SoundConfig"/> API ranks in the pool as <see cref="Default"/> + its Priority.
    /// </summary>
    public static class SoundPriority
    {
        /// <summary>Hit marks.</summary>
        public const int Feedback = 100;
        /// <summary>Own voice, radio, NPC speech.</summary>
        public const int Voice = 90;
        /// <summary>Gunshots.</summary>
        public const int Shot = 85;
        /// <summary>Horns, phone, thunder.</summary>
        public const int Signal = 70;
        /// <summary>Motors.</summary>
        public const int Engine = 60;
        /// <summary>Speakers, cinema.</summary>
        public const int Music = 55;
        /// <summary>Everything without a class.</summary>
        public const int Default = 50;
        /// <summary>Hits, gun mechanics, small clicks.</summary>
        public const int Detail = 40;
        /// <summary>Beds: ambience loops, waves, rain, wind.</summary>
        public const int Bed = 20;

        /// <summary>AudioSource.priority for <paramref name="priority"/>: 0 → 256, 50 → 128, 100 → 0.</summary>
        public static int ToUnity(int priority) =>
            Mathf.RoundToInt(256f * (1f - Mathf.Clamp(priority, 0, 100) / 100f));
    }
}
