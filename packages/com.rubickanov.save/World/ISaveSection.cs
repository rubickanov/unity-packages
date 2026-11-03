using System;
using System.Collections.Generic;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>
    /// One system's part of a world slot, kept as its own versioned document (<c>clock.000043.json</c>). The game lists
    /// its sections once, in the order they restore.
    /// </summary>
    public interface ISaveSection
    {
        /// <summary>The file's stem and the document's kind: <c>[a-z0-9-]</c>, not <c>manifest</c>.</summary>
        string Id { get; }

        /// <summary>The chain to the current version; null means version 1.</summary>
        SaveMigrations Migrations { get; }

        /// <summary>Writes the system's state over its last tree, on the main thread, in the save's frame: cheap, no text.</summary>
        void Capture(SaveWriter w);

        /// <summary>Takes the saved state, on the main thread, in the game's order; only when the slot has the section.</summary>
        void Restore(SaveReader r);
    }

    /// <summary>
    /// A section with records of their own versions inside (pieces of the world): checks and migrates them before any
    /// section restores, so a load never stops halfway. It runs off the scene and may change the tree in place.
    /// </summary>
    public interface ISaveSectionCheck
    {
        SaveCheck Check(SaveNode data);
    }

    /// <summary>
    /// A section whose records have kinds with versions of their own: the manifest lists the newest version of each kind
    /// it holds, so a build that knows a kind only at an older version refuses the slot without reading the section.
    /// </summary>
    public interface ISaveSectionKinds
    {
        /// <summary>The kinds this build knows, with their current versions.</summary>
        IReadOnlyDictionary<string, int> Known { get; }

        /// <summary>Adds the kinds in a captured tree to <paramref name="into"/>, with the highest version of each.</summary>
        void Collect(SaveNode data, Dictionary<string, int> into);
    }

    /// <summary>What <see cref="ISaveSectionCheck.Check"/> found.</summary>
    public readonly struct SaveCheck
    {
        private static readonly string[] NoNotes = Array.Empty<string>();

        private SaveCheck(SaveCheckStatus status, string message, IReadOnlyList<string> notes)
        {
            Status = status;
            Message = message;
            Notes = notes ?? NoNotes;
        }

        public static SaveCheck Ok => new(SaveCheckStatus.Ok, null, null);

        /// <summary>The data is whole; <paramref name="notes"/> go to the load's report.</summary>
        public static SaveCheck OkWith(IReadOnlyList<string> notes) => new(SaveCheckStatus.Ok, null, notes);

        /// <summary>The data cannot be used: the load falls back to the previous generation.</summary>
        public static SaveCheck Broken(string message) => new(SaveCheckStatus.Broken, message, null);

        /// <summary>The data was written by a newer build: the whole slot is refused.</summary>
        public static SaveCheck Newer(string message) => new(SaveCheckStatus.Newer, message, null);

        public SaveCheckStatus Status { get; }

        public string Message { get; }

        public IReadOnlyList<string> Notes { get; }
    }

    public enum SaveCheckStatus
    {
        Ok,
        Broken,
        Newer,
    }

    /// <summary>A section in a few lines, without a class of its own.</summary>
    public static class SaveSection
    {
        public static ISaveSection Of(string id, SaveMigrations migrations, Action<SaveWriter> capture, Action<SaveReader> restore) =>
            new Inline(id, migrations, capture, restore);

        private sealed class Inline : ISaveSection
        {
            private readonly Action<SaveWriter> _capture;
            private readonly Action<SaveReader> _restore;

            public Inline(string id, SaveMigrations migrations, Action<SaveWriter> capture, Action<SaveReader> restore)
            {
                Id = id;
                Migrations = migrations;
                _capture = capture ?? throw new ArgumentNullException(nameof(capture));
                _restore = restore ?? throw new ArgumentNullException(nameof(restore));
            }

            public string Id { get; }

            public SaveMigrations Migrations { get; }

            public void Capture(SaveWriter w) => _capture(w);

            public void Restore(SaveReader r) => _restore(r);
        }
    }
}
