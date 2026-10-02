namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// How <c>[PersistedState] ReactiveProperty&lt;TEnum&gt;</c> is encoded in the
    /// snapshot. Picked explicitly via <see cref="PersistedEnumAttribute"/>; there is
    /// no implicit default for enums because the wrong choice silently breaks old saves.
    /// </summary>
    public enum PersistedEnumMode
    {
        /// <summary>
        /// Snapshot stores the enum as a <see cref="string"/> — the member name.
        /// Resilient to reordering members and adding new ones. Only a rename breaks
        /// the save, and that's the case where a migrator is obvious. Default choice
        /// for game-save enums where authoring churn is common.
        /// </summary>
        ByName = 0,

        /// <summary>
        /// Snapshot stores the enum as its underlying integer value. Compact and fast,
        /// but reordering members or inserting before an existing one silently shifts
        /// every subsequent value and loads wrong data. Pick this only when the enum is
        /// append-only or explicitly value-stable (e.g. with explicit numeric assignments).
        /// </summary>
        ByValue = 1,
    }
}
