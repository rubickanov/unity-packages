using System;
using System.Collections.Generic;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>A slot as a list shows it, read from its newest whole manifest without reading its sections' data.</summary>
    public sealed class SlotInfo
    {
        public string Id { get; internal set; }

        public string Name { get; internal set; }

        /// <summary>The build that wrote the generation shown.</summary>
        public string Build { get; internal set; }

        /// <summary>UTC.</summary>
        public DateTime Created { get; internal set; }

        /// <summary>UTC.</summary>
        public DateTime Saved { get; internal set; }

        public TimeSpan Played { get; internal set; }

        public int Generation { get; internal set; }

        /// <summary>Section id → version.</summary>
        public IReadOnlyDictionary<string, int> Sections { get; internal set; }

        /// <summary>Kind of piece → the newest version the slot holds.</summary>
        public IReadOnlyDictionary<string, int> Kinds { get; internal set; }

        /// <summary>What the game wrote for lists (scene, day, cash).</summary>
        public SaveReader Game { get; internal set; }

        /// <summary>A picture's file name in the slot, or null.</summary>
        public string Preview { get; internal set; }

        /// <summary>Why the slot cannot be loaded nor written ("made by a newer build …"); null when it can.</summary>
        public string Refused { get; internal set; }

        /// <summary>Which generation was broken and which one is used instead; null when the newest is whole.</summary>
        public string FellBack { get; internal set; }

        /// <summary>Why nothing of the slot could be read (no whole generation); null when something could.</summary>
        public string Error { get; internal set; }

        public override string ToString() => $"{Id} \"{Name}\" gen {Generation}";
    }

    /// <summary>What a load did.</summary>
    public sealed class SaveReport
    {
        public int Generation { get; internal set; }

        /// <summary>The newest generation that was broken, when an older one was used.</summary>
        public int? FellBackFrom { get; internal set; }

        /// <summary>Why the slot is refused (made by a newer build); null when it loaded.</summary>
        public string Refused { get; internal set; }

        /// <summary>Why nothing loaded: no such slot, no whole generation, the store failed; null when it loaded.</summary>
        public string Error { get; internal set; }

        /// <summary>Unknown sections, values of the wrong kind, migrations, the fallback.</summary>
        public IReadOnlyList<string> Notes { get; internal set; }

        /// <summary>Main-thread time of every section's restore.</summary>
        public float RestoreMs { get; internal set; }

        public bool Ok => Refused == null && Error == null;

        public override string ToString() =>
            Refused != null ? $"refused: {Refused}" :
            Error != null ? $"error: {Error}" :
            $"generation {Generation}" + (FellBackFrom != null ? $" (fell back from {FellBackFrom})" : "") + $", {Notes.Count} notes";
    }
}
