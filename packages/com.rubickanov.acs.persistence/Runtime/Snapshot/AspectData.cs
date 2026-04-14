using System.Collections.Generic;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Per-aspect slice of a <see cref="AspectSnapshot"/>. Holds raw, boxed values
    /// keyed by field name. For <c>ObservableList&lt;T&gt;</c> the value is a
    /// <c>List&lt;T&gt;</c>; for <c>ObservableDictionary&lt;K,V&gt;</c> it is a
    /// <c>Dictionary&lt;K,V&gt;</c>; for <c>ObservableHashSet&lt;T&gt;</c> it is a
    /// <c>HashSet&lt;T&gt;</c>. These are the concrete CLR types a save layer's
    /// serializer expects.
    /// <para/>
    /// Iteration order of <see cref="Fields"/> is ordinal-sorted — see
    /// <see cref="AspectSnapshot"/> for the determinism rationale.
    /// </summary>
    public sealed class AspectData
    {
        /// <summary>
        /// Schema version of this aspect's persisted fields. <c>Snapshot()</c> stamps it
        /// from <see cref="PersistedVersionAttribute"/>; <c>Restore()</c> runs registered
        /// <see cref="IAspectMigrator"/> steps until this value matches the aspect's current
        /// version. Default <c>0</c> — interpreted identically to an aspect without the attribute,
        /// which keeps pre-1.2 snapshots forward-compatible.
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Field values keyed by field name. Boxed scalar for <c>ReactiveProperty&lt;T&gt;</c>;
        /// for collection kinds see the concrete CLR types described on the class.
        /// Iteration is ordinal-sorted.
        /// </summary>
        public SortedDictionary<string, object> Fields { get; }

        /// <summary>Creates an empty aspect slice. Used by <c>Snapshot()</c> before filling.</summary>
        public AspectData()
        {
            Fields = new SortedDictionary<string, object>(System.StringComparer.Ordinal);
        }

        /// <summary>
        /// Wraps an externally-constructed field map — typical after a save-layer
        /// deserializer hands the dictionary back for <c>Restore()</c>. Entries are
        /// copied into an ordinal-sorted map, so the caller's collection type does
        /// not affect determinism.
        /// </summary>
        public AspectData(IDictionary<string, object> fields)
        {
            Fields = fields == null
                ? new SortedDictionary<string, object>(System.StringComparer.Ordinal)
                : new SortedDictionary<string, object>(fields, System.StringComparer.Ordinal);
        }
    }
}
