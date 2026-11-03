using System;

namespace Rubickanov.Audio
{
    /// <summary>A loop started from a cue. Once stopped it is dead: calls with it do nothing; ids are never reused.</summary>
    public readonly struct LoopHandle : IEquatable<LoopHandle>
    {
        public static readonly LoopHandle Invalid = default;

        private readonly long _id;

        internal LoopHandle(long id) => _id = id;

        public bool IsValid => _id != 0;

        internal long Id => _id;

        public bool Equals(LoopHandle other) => _id == other._id;
        public override bool Equals(object? obj) => obj is LoopHandle other && Equals(other);
        public override int GetHashCode() => _id.GetHashCode();
        public static bool operator ==(LoopHandle left, LoopHandle right) => left._id == right._id;
        public static bool operator !=(LoopHandle left, LoopHandle right) => left._id != right._id;
        public override string ToString() => $"LoopHandle({_id})";
    }
}
