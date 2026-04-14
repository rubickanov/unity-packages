namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Whole-<see cref="AspectSnapshot"/> migrator — covers the cross-aspect shape
    /// changes that per-aspect migrators cannot express: split one aspect into two,
    /// merge two into one, delete an obsolete aspect, rename an aspect key without
    /// renaming its CLR type.
    /// <para/>
    /// Invoked once for every entity snapshot in a <see cref="WorldSnapshot"/> plus
    /// the world-scoped slot. Runs before per-aspect <see cref="IAspectMigrator"/>
    /// so downstream migrators see the rearranged shape.
    /// <para/>
    /// Triggered by <see cref="WorldSnapshot.FormatVersion"/>. Each migrator covers one
    /// step <see cref="FromFormatVersion"/> → <c>FromFormatVersion + 1</c>. The registry
    /// advances <see cref="WorldSnapshot.FormatVersion"/> on the restored snapshot.
    /// </summary>
    public interface IAspectSnapshotMigrator
    {
        /// <summary>Source format version. Migrator advances snapshots to <c>FromFormatVersion + 1</c>.</summary>
        int FromFormatVersion { get; }

        /// <summary>Transform an aspect snapshot in place — add/remove/rewrite aspect entries.</summary>
        void Migrate(AspectSnapshot snapshot);
    }
}
