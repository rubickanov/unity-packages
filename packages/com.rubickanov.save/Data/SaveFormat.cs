using System;
using System.Collections.Generic;

namespace Rubickanov.Save.Data
{
    public enum SaveReadStatus
    {
        /// <summary>Read and migrated to the current version.</summary>
        Ok,

        /// <summary>The text is not a document of this format, or a migration step failed on it: keep it aside.</summary>
        Broken,

        /// <summary>A document of another kind.</summary>
        WrongKind,

        /// <summary>Written by a newer format than this build knows: neither read nor to be written over.</summary>
        Newer,
    }

    /// <summary>
    /// One kind of document and its versions. The document is an object whose header says what it is ("$kind"), its
    /// format version ("$version") and the build that wrote it ("$build"); the other keys are its data. Reading migrates
    /// older versions to <see cref="Version"/> and refuses newer ones.
    /// </summary>
    public sealed class SaveFormat
    {
        public const string KindKey = "$kind";
        public const string VersionKey = "$version";
        public const string BuildKey = "$build";

        private readonly SaveMigrations _migrations;

        /// <param name="migrations">The chain to the current version; none means version 1.</param>
        public SaveFormat(string kind, SaveMigrations migrations = null)
        {
            if (string.IsNullOrEmpty(kind))
            {
                throw new ArgumentException("A format has a kind.", nameof(kind));
            }

            Kind = kind;
            _migrations = migrations ?? new SaveMigrations();
        }

        public string Kind { get; }

        public int Version => _migrations.Current;

        public SaveMigrations Migrations => _migrations;

        /// <summary>Parses the text, checks its header and migrates its data to <see cref="Version"/>.</summary>
        public SaveRead Read(string text)
        {
            var notes = new List<string>();
            if (!SaveJson.TryRead(text, out SaveNode root, out string error, notes))
            {
                return new SaveRead(SaveReadStatus.Broken, null, 0, null, $"{Kind}: {error}", notes);
            }

            if (!root.IsObject)
            {
                return new SaveRead(SaveReadStatus.Broken, null, 0, null, $"{Kind}: the document is not an object", notes);
            }

            SaveNode kind = root[KindKey];
            SaveNode version = root[VersionKey];
            SaveNode build = root[BuildKey];
            string madeBy = build != null && build.Kind == SaveNodeKind.String ? build.String : null;
            if (kind == null || kind.Kind != SaveNodeKind.String || kind.String != Kind)
            {
                string found = kind == null ? "no $kind" : $"$kind {kind.Describe()}";
                return new SaveRead(SaveReadStatus.WrongKind, null, 0, madeBy, $"{Kind} expected, {found} found", notes);
            }

            if (version == null || !version.IsInteger || version.Long < 1 || version.Long > int.MaxValue)
            {
                string found = version == null ? "no $version" : $"$version {version.Describe()}";
                return new SaveRead(SaveReadStatus.Broken, null, 0, madeBy, $"{Kind}: {found}", notes);
            }

            int from = (int)version.Long;
            if (from > Version)
            {
                string who = madeBy ?? "an unknown build";
                return new SaveRead(SaveReadStatus.Newer, null, from, madeBy,
                    $"made by {who} with {Kind} v{from}; this build knows v{Version}", notes);
            }

            SaveNode data = SaveNode.NewObject();
            IReadOnlyList<string> keys = root.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] != KindKey && keys[i] != VersionKey && keys[i] != BuildKey)
                {
                    data.Put(keys[i], root[keys[i]]);
                }
            }

            try
            {
                data = _migrations.Walk(data, from, notes);
            }
            catch (Exception e)
            {
                return new SaveRead(SaveReadStatus.Broken, null, from, madeBy,
                    $"{Kind} v{from} could not be migrated to v{Version}: {e.Message}", notes);
            }

            return new SaveRead(SaveReadStatus.Ok, data, from, madeBy, null, notes);
        }

        /// <summary>
        /// The canonical text of <paramref name="data"/> under this format's header at <see cref="Version"/>.
        /// Values with no JSON are reported to <paramref name="logError"/> (Unity's error log when null).
        /// </summary>
        public string Write(SaveNode data, string build, Action<string> logError = null)
        {
            SaveNode root = SaveNode.NewObject();
            if (data != null && data.IsObject)
            {
                IReadOnlyList<string> keys = data.Keys;
                for (int i = 0; i < keys.Count; i++)
                {
                    root.Put(keys[i], data[keys[i]]); // shared, not copied: the root only lives to be written
                }
            }

            root.Put(KindKey, SaveNode.Of(Kind));
            root.Put(VersionKey, SaveNode.Of(Version));
            root.Put(BuildKey, SaveNode.Of(build ?? string.Empty));
            return SaveJson.Write(root, logError);
        }
    }

    /// <summary>What <see cref="SaveFormat.Read"/> found.</summary>
    public readonly struct SaveRead
    {
        internal SaveRead(SaveReadStatus status, SaveNode data, int from, string build, string error, List<string> notes)
        {
            Status = status;
            Data = data;
            From = from;
            Build = build;
            Error = error;
            Notes = notes;
        }

        public SaveReadStatus Status { get; }

        public bool IsOk => Status == SaveReadStatus.Ok;

        /// <summary>The data at the current version, without the header; null unless <see cref="IsOk"/>.</summary>
        public SaveNode Data { get; }

        /// <summary>The version in the file.</summary>
        public int From { get; }

        /// <summary>The build that wrote the file, when it says.</summary>
        public string Build { get; }

        /// <summary>Why it is not <see cref="SaveReadStatus.Ok"/>.</summary>
        public string Error { get; }

        /// <summary>What was read leniently or changed on the way (a NaN read as null, a migration).</summary>
        public IReadOnlyList<string> Notes { get; }
    }
}
