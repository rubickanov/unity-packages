using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObservableCollections;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Reflects over an aspect instance and returns the list of <c>[PersistedState]</c>
    /// fields, cached per-type. Mirrors the shape of the netcode package's
    /// ReplicationScanner: BaseType walk, stable field order, fail-fast validation.
    /// </summary>
    internal static class PersistenceScanner
    {
        private static readonly Dictionary<Type, PersistedFieldInfo[]> Cache = new();

        // Play-Mode-without-Domain-Reload safety: static caches survive domain reload
        // but poison the first scan after a reload if we let them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
        }

        public static PersistedFieldInfo[] Scan(object aspect)
        {
            var type = aspect.GetType();
            if (Cache.TryGetValue(type, out var cached)) return cached;

            var fields = Collect(type);
            Cache[type] = fields;
            return fields;
        }

        public static bool HasPersistedFields(object aspect)
        {
            return Scan(aspect).Length > 0;
        }

        private static PersistedFieldInfo[] Collect(Type aspectType)
        {
            var result = new List<PersistedFieldInfo>();
            var current = aspectType;

            while (current != null && current != typeof(object))
            {
                var fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    var attr = field.GetCustomAttribute<PersistedStateAttribute>();
                    if (attr == null) continue;

                    if (!TryClassify(aspectType, field, out var info)) continue;
                    result.Add(info);
                }

                current = current.BaseType;
            }

            // Stable ordering: reflection field order is not guaranteed between runs,
            // and deterministic snapshots make diffs and test assertions sane.
            return result.OrderBy(f => f.Field.Name).ToArray();
        }

        private static bool TryClassify(Type aspectType, FieldInfo field, out PersistedFieldInfo info)
        {
            info = default;
            var fieldType = field.FieldType;

            if (!fieldType.IsGenericType)
            {
                LogUnsupportedContainer(aspectType, field);
                return false;
            }

            var generic = fieldType.GetGenericTypeDefinition();
            var args = fieldType.GetGenericArguments();

            if (generic == typeof(ReactiveProperty<>))
            {
                var value = args[0];
                if (!IsAllowedValueType(value))
                {
                    LogUnsupportedValueType(aspectType, field, value);
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.Reactive, value, null);
                return true;
            }

            if (generic == typeof(ObservableList<>))
            {
                var value = args[0];
                if (!IsAllowedValueType(value))
                {
                    LogUnsupportedValueType(aspectType, field, value);
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.ObservableList, value, null);
                return true;
            }

            if (generic == typeof(ObservableHashSet<>))
            {
                var value = args[0];
                if (!IsAllowedValueType(value))
                {
                    LogUnsupportedValueType(aspectType, field, value);
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.ObservableHashSet, value, null);
                return true;
            }

            if (generic == typeof(ObservableDictionary<,>))
            {
                var key = args[0];
                var value = args[1];
                if (!IsAllowedValueType(key))
                {
                    LogUnsupportedValueType(aspectType, field, key, role: "key");
                    return false;
                }
                if (!IsAllowedValueType(value))
                {
                    LogUnsupportedValueType(aspectType, field, value, role: "value");
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.ObservableDictionary, value, key);
                return true;
            }

            LogUnsupportedContainer(aspectType, field);
            return false;
        }

        /// <summary>
        /// Project rule for ReactiveProperty wrapping carried over to persisted collections:
        /// only value types and <see cref="string"/>. Anything else (classes, interfaces,
        /// arrays, collections) is forbidden — serialization of reference graphs is a
        /// save-layer concern, not ACS's.
        /// </summary>
        internal static bool IsAllowedValueType(Type t)
        {
            return t.IsValueType || t == typeof(string);
        }

        private static void LogUnsupportedContainer(Type aspectType, FieldInfo field)
        {
            Debug.LogError(
                $"[PersistenceScanner] Aspect '{aspectType.Name}' field '{field.Name}' has [PersistedState] but its type " +
                $"'{field.FieldType.Name}' is not supported. Supported: ReactiveProperty<T>, ObservableList<T>, " +
                $"ObservableHashSet<T>, ObservableDictionary<K,V>. Field is skipped.");
        }

        private static void LogUnsupportedValueType(Type aspectType, FieldInfo field, Type bad, string role = "value")
        {
            Debug.LogError(
                $"[PersistenceScanner] Aspect '{aspectType.Name}' field '{field.Name}' has [PersistedState] but its {role} " +
                $"type '{bad.Name}' is neither a value type nor string. Persisted state must stay primitive; wrap reference " +
                $"graphs on the save layer. Field is skipped.");
        }
    }
}
