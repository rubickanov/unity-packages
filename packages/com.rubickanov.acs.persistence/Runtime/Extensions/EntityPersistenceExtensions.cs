using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    public static class EntityPersistenceExtensions
    {
        // Type.FullName → Type cache for Restore. Lazy-resolved once per unknown type.
        private static readonly Dictionary<string, Type> TypeLookup = new();

        // Cached Require<T> generic method — one MethodInfo per aspect type.
        private static readonly Dictionary<Type, MethodInfo> RequireMethods = new();
        private static readonly MethodInfo RequireOpen = typeof(IEntity).GetMethod(nameof(IEntity.Require))
            ?? throw new InvalidOperationException("IEntity.Require<T>() not found via reflection.");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            TypeLookup.Clear();
            RequireMethods.Clear();
        }

        /// <summary>
        /// Collects the values of every <c>[PersistedState]</c> field on every aspect this
        /// entity currently carries. Aspects with no persisted fields are omitted. The
        /// returned object is a detachable POCO — safe to hand off to a save layer for
        /// serialization, storage, or network transport.
        /// </summary>
        public static AspectSnapshot Snapshot(this IEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var snapshot = new AspectSnapshot();

            foreach (var aspect in entity.GetAllAspects())
            {
                var fields = PersistenceScanner.Scan(aspect);
                if (fields.Length == 0) continue;

                var data = new AspectData();
                for (int i = 0; i < fields.Length; i++)
                {
                    var binding = PersistedFieldBindingFactory.Create(aspect, fields[i]);
                    data.Fields[fields[i].Field.Name] = binding.ReadValue();
                }

                snapshot.Aspects[aspect.GetType().FullName] = data;
            }

            return snapshot;
        }

        /// <summary>
        /// Writes a snapshot back into this entity. Missing aspects are created via
        /// <see cref="IEntity.Require{T}"/>. Missing fields keep their default value.
        /// Unknown field names in the snapshot are silently ignored — this is forward-
        /// compatible with older schemas. Unknown aspect types (removed/renamed since
        /// the snapshot was taken) log a warning and are skipped.
        /// <para/>
        /// Writes go through <c>ReactiveProperty.Value = ...</c> without any suppress,
        /// so UI, rules, and netcode replication see restoration as a normal write and
        /// react accordingly.
        /// </summary>
        public static void Restore(this IEntity entity, AspectSnapshot snapshot)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            foreach (var pair in snapshot.Aspects)
            {
                var aspectTypeName = pair.Key;
                var data = pair.Value;

                if (!TryResolveAspectType(aspectTypeName, out var aspectType))
                {
                    Debug.LogWarning(
                        $"[acs.persistence] Restore: aspect type '{aspectTypeName}' not found in loaded assemblies. " +
                        $"Snapshot entry skipped. This is expected if the aspect was removed or renamed since the snapshot was taken.");
                    continue;
                }

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
                    catch (InvalidCastException ex)
                    {
                        // Save format drift, e.g. an int stored back into a float field.
                        // Save layer owns serializer-level type handling; we surface the
                        // mismatch and keep going so one bad field doesn't poison the whole restore.
                        Debug.LogError(
                            $"[acs.persistence] Restore: aspect '{aspectTypeName}' field '{fieldName}' — type mismatch " +
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

        private static bool TryResolveAspectType(string fullName, out Type type)
        {
            if (TypeLookup.TryGetValue(fullName, out type)) return type != null;

            type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var candidate = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (candidate != null)
                {
                    type = candidate;
                    break;
                }
            }

            // Cache even negative lookups (null) — repeated misses don't re-scan every assembly.
            TypeLookup[fullName] = type;
            return type != null;
        }

        private static object RequireAspect(IEntity entity, Type aspectType)
        {
            if (!RequireMethods.TryGetValue(aspectType, out var method))
            {
                if (!typeof(IEntityAspect).IsAssignableFrom(aspectType))
                {
                    Debug.LogWarning(
                        $"[acs.persistence] Restore: type '{aspectType.FullName}' is not an IEntityAspect. Skipping.");
                    return null;
                }

                method = RequireOpen.MakeGenericMethod(aspectType);
                RequireMethods[aspectType] = method;
            }

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
