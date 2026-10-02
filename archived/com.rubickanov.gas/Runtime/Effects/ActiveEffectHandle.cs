using System;

namespace Rubickanov.GAS
{
    public readonly struct ActiveEffectHandle : IEquatable<ActiveEffectHandle>
    {
        public static readonly ActiveEffectHandle Invalid = default;

        private readonly int _id;

        public bool IsValid => _id > 0;

        internal ActiveEffectHandle(int id)
        {
            _id = id;
        }

        public bool Equals(ActiveEffectHandle other) => _id == other._id;
        public override bool Equals(object? obj) => obj is ActiveEffectHandle other && Equals(other);
        public override int GetHashCode() => _id;

        public static bool operator ==(ActiveEffectHandle left, ActiveEffectHandle right) => left._id == right._id;
        public static bool operator !=(ActiveEffectHandle left, ActiveEffectHandle right) => left._id != right._id;

        public override string ToString() => IsValid ? $"Effect({_id})" : "Invalid";
    }
}
