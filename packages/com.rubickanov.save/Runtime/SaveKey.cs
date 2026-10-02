using System;

namespace Rubickanov.Save
{
    /// <summary>
    /// One piece of the player's data: its area and its name there. A name is relative, its parts split by
    /// <c>/</c> (<c>s01/course01.3f2a9c.jpg</c>), none empty, <c>.</c> or <c>..</c>, and holds no <c>\</c> or <c>:</c>;
    /// an area at the root takes only single names that start with its stem (<see cref="SaveArea.Owns"/>).
    /// </summary>
    public readonly struct SaveKey : IEquatable<SaveKey>
    {
        private static readonly char[] Forbidden = { '\\', ':' };

        public SaveKey(SaveArea area, string name)
        {
            if (!area.IsValid)
            {
                throw new ArgumentException("A key needs an area", nameof(area));
            }

            if (!IsValidName(name))
            {
                throw new ArgumentException($"'{name}' is not a name of saved data", nameof(name));
            }

            if (!area.Owns(name))
            {
                throw new ArgumentException($"'{name}' is not a name of {area}: it starts with '{area.Stem}.'", nameof(name));
            }

            Area = area;
            Name = name;
        }

        public SaveArea Area { get; }

        public string Name { get; }

        /// <summary>The name's last part: <c>course01.3f2a9c.jpg</c>.</summary>
        public string Leaf => Name.Substring(Name.LastIndexOf('/') + 1);

        /// <summary>Whether <paramref name="name"/> is relative and plain, in any area.</summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Forbidden) >= 0)
            {
                return false;
            }

            int start = 0;
            while (start <= name.Length)
            {
                int end = name.IndexOf('/', start);
                if (end < 0)
                {
                    end = name.Length;
                }

                int length = end - start;
                if (length == 0 || (length == 1 && name[start] == '.') ||
                    (length == 2 && name[start] == '.' && name[start + 1] == '.'))
                {
                    return false;
                }

                start = end + 1;
            }

            return true;
        }

        public bool Equals(SaveKey other) => Area == other.Area && string.Equals(Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is SaveKey other && Equals(other);

        public override int GetHashCode() => (Area.GetHashCode() * 397) ^ (Name?.GetHashCode() ?? 0);

        public static bool operator ==(SaveKey a, SaveKey b) => a.Equals(b);

        public static bool operator !=(SaveKey a, SaveKey b) => !a.Equals(b);

        public override string ToString() => $"{Area}/{Name}";
    }
}
