using System;

namespace Rubickanov.Audio
{
    public readonly struct SoundHandle : IEquatable<SoundHandle>
    {
        public static readonly SoundHandle Invalid = default;

        private readonly long _id;

        internal SoundHandle(long id) => _id = id;

        public bool IsValid => _id != 0;

        internal long Id => _id;

        public bool Equals(SoundHandle other) => _id == other._id;
        public override bool Equals(object? obj) => obj is SoundHandle other && Equals(other);
        public override int GetHashCode() => _id.GetHashCode();
        public static bool operator ==(SoundHandle left, SoundHandle right) => left._id == right._id;
        public static bool operator !=(SoundHandle left, SoundHandle right) => left._id != right._id;
        public override string ToString() => $"SoundHandle({_id})";
    }
}
