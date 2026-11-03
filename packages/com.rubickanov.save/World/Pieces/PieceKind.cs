using System;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>A kind of piece the build knows: how to spawn one from its record, and its own version and migrations.</summary>
    public sealed class PieceKind
    {
        /// <param name="kind">The stable kind string.</param>
        /// <param name="spawn">Spawns the piece with the saved id and restores its fields; null when it cannot (the record is kept as an orphan).</param>
        /// <param name="migrations">The kind's chain to its current version; null means version 1.</param>
        public PieceKind(string kind, Func<SaveId, SaveReader, ISavePiece> spawn, SaveMigrations migrations = null)
        {
            if (string.IsNullOrEmpty(kind) || kind.IndexOf('/') >= 0)
            {
                throw new ArgumentException($"A kind is a non-empty string without '/': \"{kind}\".", nameof(kind));
            }

            Kind = kind;
            Spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
            Migrations = migrations;
        }

        public string Kind { get; }

        public SaveMigrations Migrations { get; }

        public int Version => Migrations?.Current ?? 1;

        internal Func<SaveId, SaveReader, ISavePiece> Spawn { get; }

        /// <summary>A copy of the record walked from <paramref name="from"/> to the current version, its <c>$v</c> set.</summary>
        internal SaveNode Migrate(SaveNode record, int from)
        {
            SaveNode walked = Migrations != null ? Migrations.Walk(record, from) : record.Clone();
            walked[SavePieces.VersionKey] = SaveNode.Of((long)Version);
            return walked;
        }
    }
}
