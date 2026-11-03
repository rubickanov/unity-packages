namespace Rubickanov.Audio
{
    /// <summary>Random numbers for variant picks; the service holds one.</summary>
    public interface ICueRandom
    {
        /// <summary>0 ≤ result &lt; <paramref name="count"/>.</summary>
        int Next(int count);
    }

    /// <summary>UnityEngine.Random.</summary>
    public sealed class UnityCueRandom : ICueRandom
    {
        public static readonly UnityCueRandom Shared = new();

        public int Next(int count) => UnityEngine.Random.Range(0, count);
    }

    /// <summary>What a service remembers about one cue: the last variant, starts, drops and copies playing.</summary>
    public struct CueState
    {
        // Last variant + 1, so the default state has none.
        private int _last;

        /// <summary>The last variant taken; -1 before the first.</summary>
        public int Last
        {
            readonly get => _last - 1;
            set => _last = value + 1;
        }

        /// <summary>Starts heard (played or started as a loop).</summary>
        public int Played;
        /// <summary>Starts dropped by the cue's limits or by a full pool.</summary>
        public int Dropped;
        /// <summary>Copies playing now.</summary>
        public int Playing;
        internal bool Started;
        internal double LastStart;
    }

    /// <summary>Takes a cue's variant.</summary>
    public static class CuePick
    {
        /// <summary>
        /// The variant to play, or -1 for a cue without variants. <paramref name="variant"/> ≥ 0 takes
        /// variant % count (the same on every machine); &lt; 0 follows <see cref="SoundCue.Pick"/>. Either way the
        /// result becomes <see cref="CueState.Last"/>.
        /// </summary>
        public static int Next(SoundCue cue, ref CueState state, int variant, ICueRandom random)
        {
            var variants = cue.Variants;
            int count = variants != null ? variants.Length : 0;
            if (count == 0) return -1;

            int index;
            if (variant >= 0) index = variant % count;
            else if (count == 1) index = 0;
            else
            {
                switch (cue.Pick)
                {
                    case PickMode.Sequence:
                        index = (state.Last + 1) % count;
                        break;
                    case PickMode.Random:
                        index = random.Next(count);
                        break;
                    default:
                        int last = state.Last;
                        if (last < 0 || last >= count) index = random.Next(count);
                        else
                        {
                            // One of the others, evenly: skip over the last.
                            index = random.Next(count - 1);
                            if (index >= last) index++;
                        }
                        break;
                }
            }

            state.Last = index;
            return index;
        }
    }
}
