using System;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Knobs for <c>World.RestoreAll</c>. The default value — <c>default(WorldRestoreOptions)</c>
    /// — is the safe, non-destructive choice: live entities not mentioned in the snapshot are
    /// left alone. Opt into destructive behaviour explicitly.
    /// </summary>
    public readonly struct WorldRestoreOptions
    {
        /// <summary>
        /// What to do with entities that are alive in the world but absent from the
        /// snapshot being applied. Defaults to <see cref="MissingEntityPolicy.Ignore"/>.
        /// </summary>
        public MissingEntityPolicy Missing { get; init; }

        /// <summary>
        /// Optional override invoked for each missing entity when
        /// <see cref="Missing"/> is <see cref="MissingEntityPolicy.DisposeMissing"/>.
        /// Supply this to customise teardown — e.g. return a <see cref="MonoEntity"/>
        /// to a pool instead of destroying its GameObject.
        /// <para/>
        /// When left null, the built-in fallback disposes <see cref="IDisposable"/>
        /// entities directly and calls <c>UnityEngine.Object.Destroy(component.gameObject)</c>
        /// for <c>UnityEngine.Component</c>-backed ones. Neither shape matches your
        /// implementation? Supply a callback here.
        /// </summary>
        public Action<IEntity> DisposeMissing { get; init; }

        /// <summary>
        /// Optional migration registry applied before field writes. Enables
        /// <see cref="IAspectSnapshotMigrator"/> to rewrite the snapshot structure
        /// (split/merge/delete aspects) and <see cref="IAspectMigrator"/> to evolve
        /// individual aspects' fields across <see cref="PersistedVersionAttribute"/>
        /// bumps. When <c>null</c> the package skips migrations and falls back to the
        /// legacy behaviour: missing field → default kept; unknown field → ignored;
        /// version mismatch → warning + skip of that aspect.
        /// </summary>
        public PersistenceMigrationRegistry Migrations { get; init; }
    }

    /// <summary>
    /// Policy for live-but-unreferenced entities during <c>RestoreAll</c>.
    /// </summary>
    public enum MissingEntityPolicy
    {
        /// <summary>
        /// Leave the entity alone. The snapshot is treated as an overlay — entities absent from
        /// it keep their current state. Appropriate for checkpoints and partial restores, and
        /// for scenes where decorations / statics are spawned by level loading, not by save.
        /// </summary>
        Ignore = 0,

        /// <summary>
        /// Dispose every entity that carries <c>[PersistedState]</c> and whose identity was
        /// not produced by <c>resolveOrSpawn</c> for this restore. Appropriate for "load slot
        /// from scratch": the live world is forced to exactly match the snapshot's set of
        /// persisted entities. Entities without any <c>[PersistedState]</c> field (particles,
        /// runtime-only ownership aspects) survive — they never appear in the candidate list.
        /// The <see cref="World"/> itself is never disposed, regardless of this setting.
        /// </summary>
        DisposeMissing = 1,
    }
}
