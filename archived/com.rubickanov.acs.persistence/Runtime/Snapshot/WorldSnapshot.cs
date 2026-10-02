using System.Collections.Generic;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Whole-world persisted state. Produced by <c>World.SnapshotAll(keyOf)</c> and
    /// consumed by <c>World.RestoreAll(snapshot, resolveOrSpawn, options)</c>. Detachable
    /// POCO — safe to hand off to a save layer for serialization, storage, or transport.
    /// <para/>
    /// World-scoped aspects live on the dedicated <see cref="World"/> field rather than
    /// inside <see cref="Entities"/>. The World is a singleton within a snapshot — it is
    /// never spawned by <c>resolveOrSpawn</c> and never disposed by
    /// <see cref="MissingEntityPolicy.DisposeMissing"/> — and the split shape reflects
    /// that asymmetry in the type itself.
    /// <para/>
    /// Iteration of <see cref="Entities"/> is ordinal-sorted by key. See
    /// <see cref="AspectSnapshot"/> for the determinism rationale.
    /// </summary>
    public sealed class WorldSnapshot
    {
        /// <summary>
        /// Snapshot-wide format version. Drives <see cref="IAspectSnapshotMigrator"/>
        /// chains during <c>RestoreAll</c>. Write with
        /// <c>World.SnapshotAll(keyOf, registry)</c> (stamps from
        /// <see cref="PersistenceMigrationRegistry.CurrentFormatVersion"/>); the save
        /// layer may override it to encode its own format conventions. Default <c>0</c>
        /// for pre-1.2 snapshots and for worlds saved without a registry.
        /// </summary>
        public int FormatVersion { get; set; }

        /// <summary>
        /// World-scoped aspect state. <c>null</c> when the <see cref="Runtime.World"/>
        /// carried no <c>[PersistedState]</c> fields at capture time.
        /// </summary>
        public AspectSnapshot World { get; set; }

        /// <summary>
        /// Per-entity snapshots keyed by the save layer's stable id (whatever
        /// <c>keyOf</c> returned during <c>SnapshotAll</c>). Entities without any
        /// <c>[PersistedState]</c> field are not included. Iteration is ordinal-sorted.
        /// </summary>
        public SortedDictionary<string, AspectSnapshot> Entities { get; }

        /// <summary>Creates an empty snapshot. Used by <c>SnapshotAll</c> before filling.</summary>
        public WorldSnapshot()
        {
            Entities = new SortedDictionary<string, AspectSnapshot>(System.StringComparer.Ordinal);
        }

        /// <summary>
        /// Wraps an externally-constructed map — typical after a save-layer deserializer
        /// hands the pieces back for <c>RestoreAll</c>. Entries are copied into an
        /// ordinal-sorted map, so the caller's collection type does not affect determinism.
        /// </summary>
        public WorldSnapshot(AspectSnapshot world, IDictionary<string, AspectSnapshot> entities)
        {
            World = world;
            Entities = entities == null
                ? new SortedDictionary<string, AspectSnapshot>(System.StringComparer.Ordinal)
                : new SortedDictionary<string, AspectSnapshot>(entities, System.StringComparer.Ordinal);
        }

        /// <summary>True when neither world-scoped aspects nor any entity entries were captured.</summary>
        public bool IsEmpty => World == null && Entities.Count == 0;
    }
}
