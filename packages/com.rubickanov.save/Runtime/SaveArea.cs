using System;

namespace Rubickanov.Save
{
    /// <summary>
    /// What a piece of the player's data is: the settings, the progress, saved replays. The game declares its areas
    /// once, as static fields, and names what it keeps by area and name, never by path; each store keeps an area its own
    /// way (a folder, a save container). An area also says how a store over files lays it out: in a folder of its own,
    /// or as files at the root beside other areas, which is how saves made before the store were often laid out.
    /// </summary>
    public readonly struct SaveArea : IEquatable<SaveArea>
    {
        private SaveArea(string name, string folder, string stem)
        {
            Name = name;
            Folder = folder;
            Stem = stem;
        }

        /// <summary>What the area is called: <c>Replays</c>. Stores that keep areas apart by name use it.</summary>
        public string Name { get; }

        /// <summary>The area's folder in a store over files: <c>Replays</c>, or empty for an area at the root.</summary>
        public string Folder { get; }

        /// <summary>
        /// For an area at the root, what its names start with before a dot: <c>settings</c> owns <c>settings.json</c> and
        /// <c>settings.broken.json</c>. Null for an area in a folder of its own.
        /// </summary>
        public string Stem { get; }

        /// <summary>False for <c>default</c>, which is no area.</summary>
        public bool IsValid => Name != null;

        /// <summary>
        /// An area kept in a folder of its own, named after it: <c>Replays/run.sdreplay</c>. Its names may have parts,
        /// <c>s01/cover.jpg</c>.
        /// </summary>
        public static SaveArea InFolder(string name)
        {
            RequirePart(name, nameof(name));
            return new SaveArea(name, name, null);
        }

        /// <summary>
        /// An area kept as files at the root, beside other areas: <c>settings.json</c>, <c>settings.broken.json</c>. Its
        /// names are single parts starting with <paramref name="stem"/> and a dot, so areas at the root never see each
        /// other's files.
        /// </summary>
        public static SaveArea AtRoot(string name, string stem)
        {
            RequirePart(name, nameof(name));
            RequirePart(stem, nameof(stem));
            return new SaveArea(name, "", stem);
        }

        /// <summary>
        /// Whether <paramref name="name"/> may be a name in this area: any valid name in a folder of its own, a single
        /// part starting with the stem and a dot at the root.
        /// </summary>
        public bool Owns(string name) =>
            SaveKey.IsValidName(name) &&
            (Stem == null || (name.IndexOf('/') < 0 && name.Length > Stem.Length + 1 &&
                              name.StartsWith(Stem, StringComparison.Ordinal) && name[Stem.Length] == '.'));

        public bool Equals(SaveArea other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            string.Equals(Folder, other.Folder, StringComparison.Ordinal) &&
            string.Equals(Stem, other.Stem, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is SaveArea other && Equals(other);

        public override int GetHashCode() => Name?.GetHashCode() ?? 0;

        public static bool operator ==(SaveArea a, SaveArea b) => a.Equals(b);

        public static bool operator !=(SaveArea a, SaveArea b) => !a.Equals(b);

        public override string ToString() => Name ?? "(none)";

        private static void RequirePart(string part, string parameter)
        {
            if (!SaveKey.IsValidName(part) || part.IndexOf('/') >= 0)
            {
                throw new ArgumentException($"'{part}' is not a single plain name", parameter);
            }
        }
    }
}
