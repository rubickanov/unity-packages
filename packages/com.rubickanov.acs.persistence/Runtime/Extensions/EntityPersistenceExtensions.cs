using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    public static class EntityPersistenceExtensions
    {
        // Cached Require<T> generic method — one MethodInfo per aspect type. ConcurrentDictionary
        // so concurrent Restore() calls from different threads share a lock-free hit path.
        private static readonly ConcurrentDictionary<Type, MethodInfo> RequireMethods = new();
        private static readonly MethodInfo RequireOpen = typeof(IEntity).GetMethod(nameof(IEntity.Require))
            ?? throw new InvalidOperationException("IEntity.Require<T>() not found via reflection.");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RequireMethods.Clear();
        }

        /// <summary>
        /// Collects the values of every <c>[PersistedState]</c> field on every aspect this
        /// entity currently carries. Aspects with no persisted fields are omitted. The
        /// returned object is a detachable POCO — safe to hand off to a save layer for
        /// serialization, storage, or network transport.
        /// <para/>
        /// Aspects are keyed by their stable snapshot key — <see cref="PersistedKeyAttribute"/>
        /// when present, <c>Type.FullName</c> otherwise. <see cref="AspectData.Version"/> is
        /// stamped from <see cref="PersistedVersionAttribute"/> (defaults to <c>0</c>).
        /// </summary>
        public static AspectSnapshot Snapshot(this IEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var snapshot = new AspectSnapshot();

            foreach (var aspect in entity.GetAllAspects())
            {
                var fields = PersistenceScanner.Scan(aspect);
                if (fields.Length == 0) continue;

                var aspectType = aspect.GetType();
                var data = new AspectData
                {
                    Version = PersistedKeyRegistry.VersionOf(aspectType),
                };

                for (int i = 0; i < fields.Length; i++)
                {
                    var binding = PersistedFieldBindingFactory.Create(aspect, fields[i]);
                    data.Fields[fields[i].Field.Name] = binding.ReadValue();
                }

                snapshot.Aspects[PersistedKeyRegistry.KeyOf(aspectType)] = data;
            }

            return snapshot;
        }

        /// <summary>
        /// Writes a snapshot back into this entity. Equivalent to
        /// <c>Restore(snapshot, registry: null)</c> — no migrations, legacy forward-compat
        /// behaviour: missing fields keep defaults, unknown fields are ignored, unknown
        /// aspect keys log a warning and are skipped.
        /// </summary>
        public static void Restore(this IEntity entity, AspectSnapshot snapshot)
        {
            Restore(entity, snapshot, registry: null);
        }

        /// <summary>
        /// Writes a snapshot back into this entity with optional migration support.
        /// When <paramref name="registry"/> is supplied and the snapshot's aspect
        /// <see cref="AspectData.Version"/> is below the current
        /// <see cref="PersistedVersionAttribute"/>, the registered
        /// <see cref="IAspectMigrator"/> chain runs before field writes. Writes go through
        /// <c>ReactiveProperty.Value = ...</c> without any suppress, so UI, rules, and
        /// netcode replication see restoration as a normal write.
        /// <para/>
        /// When migrations run, the snapshot is mutated in place — <see cref="AspectData.Fields"/>
        /// is rewritten by each <see cref="IAspectMigrator"/> step and <see cref="AspectData.Version"/>
        /// is advanced as steps succeed. Do not reuse the same <see cref="AspectSnapshot"/> instance
        /// for a second restore; take a fresh copy if the save layer needs to replay it.
        /// </summary>
        public static void Restore(this IEntity entity, AspectSnapshot snapshot, PersistenceMigrationRegistry registry)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            foreach (var pair in snapshot.Aspects)
            {
                var aspectKey = pair.Key;
                var data = pair.Value;

                if (!PersistedKeyRegistry.TryResolve(aspectKey, out var aspectType))
                {
                    Debug.LogWarning(
                        $"[acs.persistence] Restore: aspect type '{aspectKey}' not found in loaded assemblies. " +
                        $"Snapshot entry skipped. This is expected if the aspect was removed or renamed since the snapshot was taken.");
                    continue;
                }

                var targetVersion = PersistedKeyRegistry.VersionOf(aspectType);
                if (!TryMigrateAspect(aspectKey, aspectType, data, targetVersion, registry))
                    continue;

                var aspect = RequireAspect(entity, aspectType);
                if (aspect == null) continue;

                var fields = PersistenceScanner.Scan(aspect);
                for (int i = 0; i < fields.Length; i++)
                {
                    var fieldName = fields[i].Field.Name;
                    if (!data.Fields.TryGetValue(fieldName, out var value)) continue;

                    var binding = PersistedFieldBindingFactory.Create(aspect, fields[i]);
                    try
                    {
                        binding.WriteValue(value);
                    }
                    catch (Exception ex) when (ex is InvalidCastException || ex is NullReferenceException)
                    {
                        // Save format drift, e.g. an int stored back into a float field or a null
                        // unboxed into a non-nullable value type. Save layer owns serializer-level
                        // type handling; we surface the mismatch and keep going so one bad field
                        // doesn't poison the whole restore.
                        Debug.LogError(
                            $"[acs.persistence] Restore: aspect '{aspectKey}' field '{fieldName}' — type mismatch " +
                            $"writing {value?.GetType().Name ?? "null"}. {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// True iff at least one aspect on the entity declares a <c>[PersistedState]</c> field.
        /// Backed by the scanner's per-type cache, so repeat calls are cheap.
        /// </summary>
        public static bool HasPersistedState(this IEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            foreach (var aspect in entity.GetAllAspects())
                if (PersistenceScanner.HasPersistedFields(aspect))
                    return true;
            return false;
        }

        private static bool TryMigrateAspect(
            string aspectKey,
            Type aspectType,
            AspectData data,
            int targetVersion,
            PersistenceMigrationRegistry registry)
        {
            var fromVersion = data.Version;

            if (fromVersion == targetVersion) return true;

            if (fromVersion > targetVersion)
            {
                Debug.LogWarning(
                    $"[acs.persistence] Restore: aspect '{aspectKey}' snapshot version {fromVersion} is newer than " +
                    $"current [PersistedVersion({targetVersion})] on '{aspectType.FullName}'. Downgrade is not supported — " +
                    "snapshot entry skipped.");
                return false;
            }

            if (registry == null ||
                !registry.TryGetAspectChain(aspectKey, fromVersion, targetVersion, out var chain))
            {
                Debug.LogWarning(
                    $"[acs.persistence] Restore: aspect '{aspectKey}' needs migration from version {fromVersion} to " +
                    $"{targetVersion} but no complete IAspectMigrator chain is registered. Snapshot entry skipped.");
                return false;
            }

            for (int i = 0; i < chain.Count; i++)
            {
                try
                {
                    chain[i].Migrate(data);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[acs.persistence] Restore: IAspectMigrator for '{aspectKey}' from version " +
                        $"{chain[i].FromVersion} threw. Snapshot entry skipped. {ex}");
                    return false;
                }
                data.Version = chain[i].FromVersion + 1;
            }

            return true;
        }

        private static object RequireAspect(IEntity entity, Type aspectType)
        {
            if (!typeof(IEntityAspect).IsAssignableFrom(aspectType))
            {
                Debug.LogWarning(
                    $"[acs.persistence] Restore: type '{aspectType.FullName}' is not an IEntityAspect. Skipping.");
                return null;
            }

            var method = RequireMethods.GetOrAdd(aspectType, static t => RequireOpen.MakeGenericMethod(t));

            try
            {
                return method.Invoke(entity, null);
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError(
                    $"[acs.persistence] Restore: IEntity.Require<{aspectType.Name}>() threw. {ex.InnerException?.Message ?? ex.Message}");
                return null;
            }
        }
    }
}
