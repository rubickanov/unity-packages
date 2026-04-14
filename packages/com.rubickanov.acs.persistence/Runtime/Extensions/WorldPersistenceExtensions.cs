using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    public static class WorldPersistenceExtensions
    {
        /// <summary>
        /// Enumerates every entity registered with this world that has at least one
        /// aspect carrying a <c>[PersistedState]</c> field. The <see cref="World"/>
        /// itself registers in its own by-id index, so if world-scoped aspects have
        /// persisted fields it is included in the enumeration.
        /// <para/>
        /// Save layers typically walk this, call <c>Snapshot()</c> on each, and pair
        /// the result with their own stable-id / prefab-id scheme before writing
        /// to disk. ACS takes no position on id or storage.
        /// </summary>
        public static IEnumerable<IEntity> PersistedEntities(this World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            foreach (var entity in world.Registry.AllEntities)
                if (entity.HasPersistedState())
                    yield return entity;
        }

        /// <summary>
        /// Captures the full persisted state of the world in a single detachable
        /// <see cref="WorldSnapshot"/>. World-scoped aspects are written to
        /// <see cref="WorldSnapshot.World"/>; every other entity with
        /// <c>[PersistedState]</c> fields is written to <see cref="WorldSnapshot.Entities"/>
        /// under the id returned by <paramref name="keyOf"/>.
        /// <para/>
        /// <paramref name="keyOf"/> is only invoked for non-world entities — the World
        /// lives on its own structural slot in the snapshot and needs no id. A
        /// <c>null</c> return or a duplicate id is a save-layer bug; both throw
        /// rather than silently losing state.
        /// </summary>
        public static WorldSnapshot SnapshotAll(this World world, Func<IEntity, string> keyOf)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (keyOf == null) throw new ArgumentNullException(nameof(keyOf));

            var result = new WorldSnapshot();

            IEntity worldEntity = world;
            if (worldEntity.HasPersistedState())
                result.World = worldEntity.Snapshot();

            foreach (var entity in world.PersistedEntities())
            {
                if (ReferenceEquals(entity, world)) continue;

                var key = keyOf(entity);
                if (key == null)
                    throw new ArgumentException(
                        $"[acs.persistence] SnapshotAll: keyOf returned null for entity '{entity}'. " +
                        "Save layers must provide a stable, non-null id for every persisted entity.",
                        nameof(keyOf));

                if (result.Entities.ContainsKey(key))
                    throw new InvalidOperationException(
                        $"[acs.persistence] SnapshotAll: duplicate key '{key}' returned by keyOf. " +
                        "Two live entities share the same id — a save layer id collision, refusing to overwrite.");

                result.Entities[key] = entity.Snapshot();
            }

            return result;
        }

        /// <summary>
        /// Applies a <see cref="WorldSnapshot"/> to the world. World-scoped aspects are
        /// restored onto this <see cref="World"/> directly. Each entry in
        /// <see cref="WorldSnapshot.Entities"/> is resolved through
        /// <paramref name="resolveOrSpawn"/> — the save layer either looks up an existing
        /// entity by the stored id or spawns a new one (prefab lookup is its concern) —
        /// and has its state restored through <c>Entity.Restore</c>.
        /// <para/>
        /// <paramref name="options"/> — default <see cref="MissingEntityPolicy.Ignore"/> — decides
        /// the fate of entities that live in the world but are absent from the snapshot.
        /// See <see cref="MissingEntityPolicy"/>.
        /// <para/>
        /// If <paramref name="resolveOrSpawn"/> returns the same <see cref="IEntity"/>
        /// instance for two distinct keys in the snapshot, the second restore overwrites
        /// the first — that is a save-layer mapping bug, ACS does not second-guess it.
        /// A <c>null</c> return is treated as "entity unavailable this session" and
        /// surfaces a warning without failing the whole restore.
        /// </summary>
        public static void RestoreAll(
            this World world,
            WorldSnapshot snapshot,
            Func<string, IEntity> resolveOrSpawn,
            WorldRestoreOptions options = default)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (resolveOrSpawn == null) throw new ArgumentNullException(nameof(resolveOrSpawn));

            if (snapshot.World != null)
                ((IEntity)world).Restore(snapshot.World);

            var restored = new HashSet<IEntity>();
            foreach (var pair in snapshot.Entities)
            {
                var key = pair.Key;
                var entitySnap = pair.Value;

                var entity = resolveOrSpawn(key);
                if (entity == null)
                {
                    Debug.LogWarning(
                        $"[acs.persistence] RestoreAll: resolveOrSpawn returned null for key '{key}'. " +
                        "Entry skipped — save layer reported the entity as unavailable.");
                    continue;
                }

                entity.Restore(entitySnap);
                restored.Add(entity);
            }

            if (options.Missing != MissingEntityPolicy.DisposeMissing) return;

            // Snapshot the candidate list before mutating — teardown unregisters the entity,
            // so iterating world.PersistedEntities() directly would invalidate enumeration.
            var toDispose = new List<IEntity>();
            foreach (var entity in world.PersistedEntities())
            {
                if (ReferenceEquals(entity, world)) continue;
                if (restored.Contains(entity)) continue;
                toDispose.Add(entity);
            }

            var disposer = options.DisposeMissing ?? DefaultDispose;
            for (int i = 0; i < toDispose.Count; i++)
                disposer(toDispose[i]);
        }

        // IEntity has no Dispose() on the interface — only the pure POCO Entity implements IDisposable,
        // and MonoEntity lives on a GameObject destroyed through UnityEngine.Object.Destroy. This dispatch
        // covers both shipped shapes; anything exotic should supply WorldRestoreOptions.DisposeMissing.
        private static void DefaultDispose(IEntity entity)
        {
            if (entity is IDisposable disposable)
            {
                disposable.Dispose();
                return;
            }

            if (entity is Component component)
            {
                UnityEngine.Object.Destroy(component.gameObject);
                return;
            }

            Debug.LogError(
                $"[acs.persistence] RestoreAll: no default teardown for entity of type '{entity.GetType().FullName}'. " +
                "Supply WorldRestoreOptions.DisposeMissing to handle this case.");
        }
    }
}
