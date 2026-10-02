using System;
using System.Collections.Concurrent;
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
    /// <para/>
    /// <b>Thread safety:</b> the per-type cache is a <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// so concurrent <see cref="Scan"/> calls from different threads (e.g. headless
    /// simulations running two worlds in parallel) are lock-free on the hit path and
    /// safe on the miss path.
    /// </summary>
    internal static class PersistenceScanner
    {
        private static readonly ConcurrentDictionary<Type, PersistedFieldInfo[]> Cache = new();

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
            return Cache.GetOrAdd(type, static t => Collect(t, onError: null));
        }

        public static bool HasPersistedFields(object aspect)
        {
            return Scan(aspect).Length > 0;
        }

        /// <summary>
        /// Runs the same classification rules as <see cref="Scan"/> against
        /// <paramref name="aspectType"/> without consulting or populating the cache, and without
        /// routing errors through <see cref="Debug.LogError"/>. Collected errors are returned to
        /// the caller so validation tooling (e.g. <c>PersistenceDebug</c>) can surface them at
        /// bootstrap time rather than on first snapshot.
        /// </summary>
        internal static IReadOnlyList<string> CollectValidationErrors(Type aspectType)
        {
            var errors = new List<string>();
            Collect(aspectType, errors.Add);
            return errors;
        }

        // Shared walk used by both cached-Scan and validation paths. When onError is null,
        // misclassified fields are reported via Debug.LogError; when non-null, the caller
        // collects error strings without logging.
        private static PersistedFieldInfo[] Collect(Type aspectType, Action<string> onError)
        {
            Action<string> reporter = onError ?? (msg => Debug.LogError(msg));

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

                    if (!TryClassify(aspectType, field, out var info, out var error))
                    {
                        reporter(error);
                        continue;
                    }
                    result.Add(info);
                }

                current = current.BaseType;
            }

            // Stable ordering: reflection field order is not guaranteed between runs,
            // and deterministic snapshots make diffs and test assertions sane.
            return result.OrderBy(f => f.Field.Name).ToArray();
        }

        private static bool TryClassify(Type aspectType, FieldInfo field, out PersistedFieldInfo info, out string error)
        {
            info = default;
            error = null;
            var fieldType = field.FieldType;

            if (!fieldType.IsGenericType)
            {
                error = BuildUnsupportedContainerMsg(aspectType, field);
                return false;
            }

            var generic = fieldType.GetGenericTypeDefinition();
            var args = fieldType.GetGenericArguments();

            if (generic == typeof(ReactiveProperty<>))
            {
                var value = args[0];
                if (value.IsEnum)
                {
                    var enumAttr = field.GetCustomAttribute<PersistedEnumAttribute>(inherit: false);
                    if (enumAttr == null)
                    {
                        error = BuildEnumRequiresAttributeMsg(aspectType, field, value);
                        return false;
                    }

                    info = new PersistedFieldInfo(field, PersistedFieldKind.Enum, value, null, enumAttr.Mode);
                    return true;
                }

                if (!IsAllowedValueType(value))
                {
                    error = BuildUnsupportedValueTypeMsg(aspectType, field, value);
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.Reactive, value, null);
                return true;
            }

            if (generic == typeof(ObservableList<>))
            {
                var value = args[0];
                if (value.IsEnum) { error = BuildEnumInCollectionMsg(aspectType, field, value); return false; }
                if (!IsAllowedValueType(value))
                {
                    error = BuildUnsupportedValueTypeMsg(aspectType, field, value);
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.ObservableList, value, null);
                return true;
            }

            if (generic == typeof(ObservableHashSet<>))
            {
                var value = args[0];
                if (value.IsEnum) { error = BuildEnumInCollectionMsg(aspectType, field, value); return false; }
                if (!IsAllowedValueType(value))
                {
                    error = BuildUnsupportedValueTypeMsg(aspectType, field, value);
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.ObservableHashSet, value, null);
                return true;
            }

            if (generic == typeof(ObservableDictionary<,>))
            {
                var key = args[0];
                var value = args[1];
                if (key.IsEnum) { error = BuildEnumInCollectionMsg(aspectType, field, key, role: "key"); return false; }
                if (value.IsEnum) { error = BuildEnumInCollectionMsg(aspectType, field, value, role: "value"); return false; }
                if (!IsAllowedValueType(key))
                {
                    error = BuildUnsupportedValueTypeMsg(aspectType, field, key, role: "key");
                    return false;
                }
                if (!IsAllowedValueType(value))
                {
                    error = BuildUnsupportedValueTypeMsg(aspectType, field, value, role: "value");
                    return false;
                }

                info = new PersistedFieldInfo(field, PersistedFieldKind.ObservableDictionary, value, key);
                return true;
            }

            error = BuildUnsupportedContainerMsg(aspectType, field);
            return false;
        }

        /// <summary>
        /// Project rule for ReactiveProperty wrapping carried over to persisted collections:
        /// only non-enum value types and <see cref="string"/>. Enums take a separate path
        /// via <see cref="PersistedEnumAttribute"/> — encoding choice (name vs value) has
        /// save-stability implications and must be explicit. Reference graphs are a
        /// save-layer concern, not ACS's.
        /// </summary>
        internal static bool IsAllowedValueType(Type t)
        {
            if (t.IsEnum) return false;
            return t.IsValueType || t == typeof(string);
        }

        private static string BuildUnsupportedContainerMsg(Type aspectType, FieldInfo field) =>
            $"[PersistenceScanner] Aspect '{aspectType.Name}' field '{field.Name}' has [PersistedState] but its type " +
            $"'{field.FieldType.Name}' is not supported. Supported: ReactiveProperty<T>, ObservableList<T>, " +
            $"ObservableHashSet<T>, ObservableDictionary<K,V>. Field is skipped.";

        private static string BuildUnsupportedValueTypeMsg(Type aspectType, FieldInfo field, Type bad, string role = "value") =>
            $"[PersistenceScanner] Aspect '{aspectType.Name}' field '{field.Name}' has [PersistedState] but its {role} " +
            $"type '{bad.Name}' is neither a value type nor string. Persisted state must stay primitive; wrap reference " +
            $"graphs on the save layer. Field is skipped.";

        private static string BuildEnumRequiresAttributeMsg(Type aspectType, FieldInfo field, Type enumType) =>
            $"[PersistenceScanner] Aspect '{aspectType.Name}' field '{field.Name}' has [PersistedState] but its enum " +
            $"type '{enumType.Name}' has no [PersistedEnum] attribute. Decide explicitly: [PersistedEnum(PersistedEnumMode.ByName)] " +
            $"(default, safe for reorder) or [PersistedEnum(PersistedEnumMode.ByValue)] (compact, reorder breaks old saves). " +
            $"Field is skipped.";

        private static string BuildEnumInCollectionMsg(Type aspectType, FieldInfo field, Type enumType, string role = "value") =>
            $"[PersistenceScanner] Aspect '{aspectType.Name}' field '{field.Name}' has [PersistedState] but its {role} " +
            $"type '{enumType.Name}' is an enum inside a collection — not supported in the current iteration. Wrap the enum " +
            $"in a plain int / string on the aspect, or open an issue with the use case. Field is skipped.";
    }
}
