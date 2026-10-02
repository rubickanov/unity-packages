using System;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Lightweight hierarchical tag identifier. 4-byte index-based struct.
    /// </summary>
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        /// <summary>Invalid/empty tag (index 0).</summary>
        public static readonly GameplayTag None = default;

        /// <summary>Index into the <see cref="GameplayTagRegistry"/>. 0 = None, valid >= 1.</summary>
        public int Index { get; }

        /// <summary>True if this tag has been assigned a valid index.</summary>
        public bool IsValid => Index > 0;

        internal GameplayTag(int index)
        {
            Index = index;
        }

        /// <summary>
        /// Hierarchical match: returns true if this tag is or descends from <paramref name="parent"/>.
        /// Requires an installed registry; throws <see cref="InvalidOperationException"/> otherwise
        /// (except when either operand is <see cref="None"/>, which short-circuits to false).
        /// </summary>
        public bool Matches(GameplayTag parent)
        {
            if (Index <= 0 || parent.Index <= 0)
                return false;

            return GameplayTagRegistry.Instance.Matches(this, parent);
        }

        /// <summary>Exact match: returns true only if indices are identical.</summary>
        public bool MatchesExact(GameplayTag other) => Index == other.Index;

        public bool Equals(GameplayTag other) => Index == other.Index;

        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);

        public override int GetHashCode() => Index;

        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Index == right.Index;

        public static bool operator !=(GameplayTag left, GameplayTag right) => left.Index != right.Index;

        public override string ToString()
        {
            if (Index <= 0)
                return "None";

            if (!GameplayTagRegistry.IsInstalled)
                return $"GameplayTag({Index})";

            return GameplayTagRegistry.Instance.GetName(this);
        }
    }
}
