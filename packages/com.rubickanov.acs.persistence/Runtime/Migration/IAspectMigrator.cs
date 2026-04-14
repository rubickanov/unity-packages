namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// A single version step for one aspect's persisted fields. Implementations mutate
    /// <see cref="AspectData.Fields"/> in place — rename entries, compute defaults,
    /// convert types, filter collections. The package advances
    /// <see cref="AspectData.Version"/> after each step; migrators must not touch it.
    /// <para/>
    /// Each migrator covers exactly one step: <see cref="FromVersion"/> → <c>FromVersion + 1</c>.
    /// Longer jumps are composed by registering a chain of single-step migrators. Keeping
    /// steps one-at-a-time makes the registry's chain resolution trivial and keeps
    /// migrators independently testable.
    /// </summary>
    public interface IAspectMigrator
    {
        /// <summary>
        /// Snapshot key of the target aspect — must match the value produced by
        /// <see cref="PersistedKeyAttribute"/> (or <c>Type.FullName</c> when the aspect
        /// has no attribute). The registry uses this to route migrators.
        /// </summary>
        string AspectKey { get; }

        /// <summary>Source version. Migrator advances data to <c>FromVersion + 1</c>.</summary>
        int FromVersion { get; }

        /// <summary>Transform the per-aspect slice in place.</summary>
        void Migrate(AspectData data);
    }
}
