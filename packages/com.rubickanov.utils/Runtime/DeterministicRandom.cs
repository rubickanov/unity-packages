namespace Rubickanov.Utils
{
    /// <summary>
    /// Deterministic hash-based random number generation using the murmur3 finalizer.
    /// Produces the same output for the same inputs on any machine — safe for network synchronization.
    /// </summary>
    public static class DeterministicRandom
    {
        private const float INV_2_24 = 1f / 16777216f; // 1 / 2^24

        /// <summary>
        /// Returns a deterministic hash from two integer keys.
        /// Based on murmur3 finalizer — ensures strong avalanche (one input bit flip ≈ 50% output bits flip).
        /// </summary>
        public static uint Hash(uint a, uint b)
        {
            uint h = a ^ (b * 2654435761u);
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return h;
        }

        /// <summary>Returns a deterministic hash from three integer keys.</summary>
        public static uint Hash(uint a, uint b, uint c)
        {
            return Hash(Hash(a, b), c);
        }

        /// <summary>Returns a deterministic hash from four integer keys.</summary>
        public static uint Hash(uint a, uint b, uint c, uint d)
        {
            return Hash(Hash(a, b), Hash(c, d));
        }

        /// <summary>
        /// Returns a deterministic float in [0, 1) from two integer keys.
        /// Uses top 24 bits for uniform distribution matching float mantissa precision.
        /// </summary>
        public static float Float01(uint a, uint b)
        {
            return (Hash(a, b) >> 8) * INV_2_24;
        }

        /// <summary>Returns a deterministic float in [0, 1) from three integer keys.</summary>
        public static float Float01(uint a, uint b, uint c)
        {
            return (Hash(a, b, c) >> 8) * INV_2_24;
        }

        /// <summary>Returns a deterministic float in [min, max) from two integer keys.</summary>
        public static float Range(uint a, uint b, float min, float max)
        {
            return min + Float01(a, b) * (max - min);
        }

        /// <summary>Returns a deterministic float in [min, max) from three integer keys.</summary>
        public static float Range(uint a, uint b, uint c, float min, float max)
        {
            return min + Float01(a, b, c) * (max - min);
        }

        /// <summary>Returns a deterministic int in [min, max) from two integer keys.</summary>
        public static int Int(uint a, uint b, int min, int maxExclusive)
        {
            return min + (int)(Hash(a, b) % (uint)(maxExclusive - min));
        }

        /// <summary>Returns a deterministic int in [min, max) from three integer keys.</summary>
        public static int Int(uint a, uint b, uint c, int min, int maxExclusive)
        {
            return min + (int)(Hash(a, b, c) % (uint)(maxExclusive - min));
        }

        /// <summary>Returns a deterministic boolean (50/50).</summary>
        public static bool Bool(uint a, uint b)
        {
            return (Hash(a, b) & 1u) == 1u;
        }

        /// <summary>Returns -1f or 1f deterministically.</summary>
        public static float Sign(uint a, uint b)
        {
            return Bool(a, b) ? 1f : -1f;
        }
    }
}
