using System;

namespace Rubickanov.Save.World
{
    /// <summary>
    /// A thing's id in the world, given once when it is first created and kept across sessions (never a session's network
    /// id). Its text is 32 lowercase hex digits.
    /// </summary>
    public readonly struct SaveId : IEquatable<SaveId>
    {
        private readonly Guid _guid;

        private SaveId(Guid guid) => _guid = guid;

        public static SaveId New() => new(Guid.NewGuid());

        /// <summary>Reads the 32-hex text; anything else (dashes, braces, the empty id) is not a SaveId.</summary>
        public static bool TryParse(string text, out SaveId id)
        {
            if (text != null && text.Length == 32 && Guid.TryParseExact(text, "N", out Guid guid) && guid != Guid.Empty)
            {
                id = new SaveId(guid);
                return true;
            }

            id = default;
            return false;
        }

        public bool IsEmpty => _guid == Guid.Empty;

        public bool Equals(SaveId other) => _guid.Equals(other._guid);

        public override bool Equals(object obj) => obj is SaveId other && Equals(other);

        public override int GetHashCode() => _guid.GetHashCode();

        public override string ToString() => _guid.ToString("N");

        public static bool operator ==(SaveId a, SaveId b) => a.Equals(b);

        public static bool operator !=(SaveId a, SaveId b) => !a.Equals(b);
    }
}
