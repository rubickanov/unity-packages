using System;
using System.Collections.Generic;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Save-layer-owned catalogue of <see cref="IAspectMigrator"/> and
    /// <see cref="IAspectSnapshotMigrator"/> instances. The package uses it during
    /// <c>Restore</c> / <c>RestoreAll</c>; the save layer registers concrete migrators.
    /// <para/>
    /// Migrators advance state one version at a time. The registry resolves a linear
    /// chain for a given <c>from → to</c> gap and rejects duplicate registrations at
    /// the same step (a duplicate is a save-layer bug, not a runtime data condition).
    /// <para/>
    /// <b>Thread safety:</b> build-once, read-many. Register every <see cref="AddAspect"/>
    /// / <see cref="AddSnapshot"/> on bootstrap (Unity main thread) before any
    /// <c>Restore</c> / <c>RestoreAll</c> fires; the resolve paths
    /// (<see cref="TryGetAspectChain"/>, <see cref="TryGetSnapshotChain"/>,
    /// <see cref="CurrentFormatVersion"/>) are lock-free reads and safe from any thread
    /// after registration is done. Registering concurrently with a restore is a data race.
    /// </summary>
    public sealed class PersistenceMigrationRegistry
    {
        // (AspectKey, FromVersion) → migrator. At most one per step.
        private readonly Dictionary<(string, int), IAspectMigrator> _aspect = new();

        // FromFormatVersion → snapshot migrator. At most one per step.
        private readonly Dictionary<int, IAspectSnapshotMigrator> _snapshot = new();

        private int _currentFormatVersion;

        /// <summary>
        /// Maximum <c>FromFormatVersion + 1</c> among registered snapshot migrators, or
        /// <c>0</c> when none are registered. Used by
        /// <c>World.SnapshotAll(keyOf, registry)</c> to stamp the saved snapshot.
        /// </summary>
        public int CurrentFormatVersion => _currentFormatVersion;

        public PersistenceMigrationRegistry AddAspect(IAspectMigrator migrator)
        {
            if (migrator == null) throw new ArgumentNullException(nameof(migrator));
            if (string.IsNullOrEmpty(migrator.AspectKey))
                throw new ArgumentException("IAspectMigrator.AspectKey must be a non-empty string.", nameof(migrator));
            if (migrator.FromVersion < 0)
                throw new ArgumentException("IAspectMigrator.FromVersion must be >= 0.", nameof(migrator));

            var slot = (migrator.AspectKey, migrator.FromVersion);
            if (_aspect.ContainsKey(slot))
                throw new InvalidOperationException(
                    $"[acs.persistence] Migration registry already has an IAspectMigrator for key '{migrator.AspectKey}' " +
                    $"from version {migrator.FromVersion}. Each (AspectKey, FromVersion) pair may appear at most once.");

            _aspect[slot] = migrator;
            return this;
        }

        public PersistenceMigrationRegistry AddSnapshot(IAspectSnapshotMigrator migrator)
        {
            if (migrator == null) throw new ArgumentNullException(nameof(migrator));
            if (migrator.FromFormatVersion < 0)
                throw new ArgumentException("IAspectSnapshotMigrator.FromFormatVersion must be >= 0.", nameof(migrator));

            if (_snapshot.ContainsKey(migrator.FromFormatVersion))
                throw new InvalidOperationException(
                    $"[acs.persistence] Migration registry already has an IAspectSnapshotMigrator from format version " +
                    $"{migrator.FromFormatVersion}. Each FromFormatVersion may appear at most once.");

            _snapshot[migrator.FromFormatVersion] = migrator;

            var nextVersion = migrator.FromFormatVersion + 1;
            if (nextVersion > _currentFormatVersion)
                _currentFormatVersion = nextVersion;

            return this;
        }

        /// <summary>
        /// Resolves the ordered chain of per-aspect migrators that bridge
        /// <paramref name="fromVersion"/> to <paramref name="toVersion"/> for
        /// <paramref name="aspectKey"/>. Returns <c>true</c> iff every intermediate
        /// step is registered. When the gap is zero, the chain is empty and the result is <c>true</c>.
        /// </summary>
        public bool TryGetAspectChain(
            string aspectKey,
            int fromVersion,
            int toVersion,
            out IReadOnlyList<IAspectMigrator> chain)
        {
            if (fromVersion == toVersion)
            {
                chain = Array.Empty<IAspectMigrator>();
                return true;
            }

            if (fromVersion > toVersion)
            {
                chain = null;
                return false;
            }

            var list = new List<IAspectMigrator>(toVersion - fromVersion);
            for (int v = fromVersion; v < toVersion; v++)
            {
                if (!_aspect.TryGetValue((aspectKey, v), out var step))
                {
                    chain = null;
                    return false;
                }
                list.Add(step);
            }

            chain = list;
            return true;
        }

        /// <summary>
        /// Resolves the ordered chain of snapshot migrators for a <c>fromVersion → toVersion</c> gap.
        /// </summary>
        public bool TryGetSnapshotChain(
            int fromVersion,
            int toVersion,
            out IReadOnlyList<IAspectSnapshotMigrator> chain)
        {
            if (fromVersion == toVersion)
            {
                chain = Array.Empty<IAspectSnapshotMigrator>();
                return true;
            }

            if (fromVersion > toVersion)
            {
                chain = null;
                return false;
            }

            var list = new List<IAspectSnapshotMigrator>(toVersion - fromVersion);
            for (int v = fromVersion; v < toVersion; v++)
            {
                if (!_snapshot.TryGetValue(v, out var step))
                {
                    chain = null;
                    return false;
                }
                list.Add(step);
            }

            chain = list;
            return true;
        }
    }
}
