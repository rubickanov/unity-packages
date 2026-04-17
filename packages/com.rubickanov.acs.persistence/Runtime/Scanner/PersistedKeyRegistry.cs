using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Resolves the stable snapshot key of an aspect type and its schema version. Keys come
    /// from <see cref="PersistedKeyAttribute"/> when present, <c>Type.FullName</c> otherwise.
    /// A reverse index (key/alias → type) is built lazily on the first miss by scanning
    /// every <see cref="IEntityAspect"/>-implementing type in loaded assemblies.
    /// <para/>
    /// Collisions — two aspects claiming the same <see cref="PersistedKeyAttribute"/>, or an
    /// alias shadowing another aspect's canonical key — log an error at index-build time and
    /// the later entrant is ignored. First registration wins, deterministically by the order
    /// <see cref="AppDomain.GetAssemblies"/> returns them.
    /// <para/>
    /// <b>Thread safety:</b> all caches are <see cref="ConcurrentDictionary{TKey,TValue}"/>;
    /// the reverse index is wrapped in <see cref="Lazy{T}"/> with
    /// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> so concurrent first
    /// <see cref="TryResolve"/> calls run the assembly sweep at most once. Read paths are
    /// lock-free, which matters when <c>SnapshotAll</c> or background deserialization runs
    /// off Unity's main thread.
    /// </summary>
    internal static class PersistedKeyRegistry
    {
        private static readonly ConcurrentDictionary<Type, string> KeyByType = new();
        private static readonly ConcurrentDictionary<Type, int> VersionByType = new();

        private static Lazy<ConcurrentDictionary<string, Type>> _reverseIndex = CreateLazyIndex();

        private static Lazy<ConcurrentDictionary<string, Type>> CreateLazyIndex()
            => new Lazy<ConcurrentDictionary<string, Type>>(BuildReverseIndex, LazyThreadSafetyMode.ExecutionAndPublication);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            KeyByType.Clear();
            VersionByType.Clear();
            Interlocked.Exchange(ref _reverseIndex, CreateLazyIndex());
        }

        /// <summary>
        /// Test-only hook — same behaviour as <see cref="ResetStatics"/> but callable from NUnit.
        /// </summary>
        internal static void ResetForTests() => ResetStatics();

        /// <summary>
        /// Test-only hook. Replaces the reverse index with an empty map so a test can
        /// seed colliding entries deterministically through <see cref="TestOnly_Register"/>
        /// without depending on whatever attributes happen to live in loaded assemblies.
        /// </summary>
        internal static void TestOnly_SeedEmptyReverseIndex()
        {
            var empty = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);
            var seeded = new Lazy<ConcurrentDictionary<string, Type>>(
                () => empty, LazyThreadSafetyMode.ExecutionAndPublication);
            _ = seeded.Value; // force materialization so TestOnly_Register shares the instance.
            Interlocked.Exchange(ref _reverseIndex, seeded);
        }

        /// <summary>
        /// Test-only hook. Routes through the same collision-logging path production uses.
        /// Requires <see cref="TestOnly_SeedEmptyReverseIndex"/> first.
        /// </summary>
        internal static void TestOnly_Register(string key, Type type, string role)
        {
            TryRegister(_reverseIndex.Value, key, type, role);
        }

        public static string KeyOf(Type aspectType)
        {
            if (aspectType == null) throw new ArgumentNullException(nameof(aspectType));
            return KeyByType.GetOrAdd(aspectType, static t =>
                t.GetCustomAttribute<PersistedKeyAttribute>(inherit: false)?.Key ?? t.FullName);
        }

        public static int VersionOf(Type aspectType)
        {
            if (aspectType == null) throw new ArgumentNullException(nameof(aspectType));
            return VersionByType.GetOrAdd(aspectType, static t =>
                t.GetCustomAttribute<PersistedVersionAttribute>(inherit: false)?.Version ?? 0);
        }

        /// <summary>
        /// Resolves a snapshot key to an aspect <see cref="Type"/>. Looks through
        /// <see cref="PersistedKeyAttribute"/> values, then <see cref="PersistedAliasAttribute"/>
        /// values, and finally falls back to <c>Type.FullName</c> via a cached assembly sweep.
        /// </summary>
        public static bool TryResolve(string key, out Type aspectType)
        {
            if (key == null)
            {
                aspectType = null;
                return false;
            }

            var index = _reverseIndex.Value;
            if (index.TryGetValue(key, out aspectType)) return aspectType != null;

            index.TryAdd(key, null); // cache negative lookups — repeated misses don't re-scan.
            aspectType = null;
            return false;
        }

        private static ConcurrentDictionary<string, Type> BuildReverseIndex()
        {
            var index = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                if (types == null) continue;

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null) continue;
                    if (t.IsInterface || t.IsAbstract) continue;
                    if (!typeof(IEntityAspect).IsAssignableFrom(t)) continue;

                    var keyAttr = t.GetCustomAttribute<PersistedKeyAttribute>(inherit: false);
                    if (keyAttr != null) TryRegister(index, keyAttr.Key, t, role: "[PersistedKey]");

                    var aliasAttrs = t.GetCustomAttributes<PersistedAliasAttribute>(inherit: false);
                    foreach (var alias in aliasAttrs)
                        TryRegister(index, alias.OldKey, t, role: "[PersistedAlias]");

                    // Register Type.FullName up front so TryResolve doesn't need to scan assemblies
                    // on a miss. Collision path (key claimed by a [PersistedKey] on another type) keeps
                    // the first registration — intentional: explicit attributes win over name fallback.
                    if (t.FullName != null) TryRegister(index, t.FullName, t, role: "Type.FullName");
                }
            }

            return index;
        }

        private static void TryRegister(ConcurrentDictionary<string, Type> index, string key, Type type, string role)
        {
            // Called from the Lazy<T> initializer (exclusive under ExecutionAndPublication) and from
            // TestOnly_Register (single-threaded test code). A plain check-then-add is safe here.
            if (index.TryGetValue(key, out var existing) && existing != null && existing != type)
            {
                Debug.LogError(
                    $"[acs.persistence] PersistedKeyRegistry: key '{key}' claimed by '{type.FullName}' via {role}, " +
                    $"but already registered to '{existing.FullName}'. The later entry is ignored — fix the collision.");
                return;
            }

            index[key] = type;
        }

        /// <summary>
        /// Enumerates the current reverse-index entries. Used by <c>PersistenceDebug.ListPersistedKeys</c>
        /// to dump what the registry will actually resolve, including <c>Type.FullName</c> fallbacks.
        /// Entries with a null value (cached negative lookups) are filtered out.
        /// </summary>
        internal static IEnumerable<KeyValuePair<string, Type>> EnumerateReverseIndex()
        {
            foreach (var pair in _reverseIndex.Value)
                if (pair.Value != null) yield return pair;
        }

        /// <summary>
        /// Re-walks every <see cref="IEntityAspect"/> type in loaded assemblies and reports keys
        /// that are claimed by more than one distinct type via <see cref="PersistedKeyAttribute"/>
        /// or <see cref="PersistedAliasAttribute"/>. Unlike the Lazy-built index — which logs the
        /// first collision and drops the later entrant — this method returns the full list of
        /// claimants for every offending key.
        /// </summary>
        internal static IReadOnlyList<(string Key, Type[] Claimants)> FindCollisions()
        {
            var claimsByKey = new Dictionary<string, List<Type>>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                if (types == null) continue;

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null) continue;
                    if (t.IsInterface || t.IsAbstract) continue;
                    if (!typeof(IEntityAspect).IsAssignableFrom(t)) continue;

                    var keyAttr = t.GetCustomAttribute<PersistedKeyAttribute>(inherit: false);
                    if (keyAttr != null) Record(claimsByKey, keyAttr.Key, t);

                    foreach (var alias in t.GetCustomAttributes<PersistedAliasAttribute>(inherit: false))
                        Record(claimsByKey, alias.OldKey, t);
                }
            }

            var result = new List<(string, Type[])>();
            foreach (var pair in claimsByKey)
                if (pair.Value.Count > 1) result.Add((pair.Key, pair.Value.ToArray()));
            return result;
        }

        private static void Record(Dictionary<string, List<Type>> claims, string key, Type t)
        {
            if (!claims.TryGetValue(key, out var list))
            {
                list = new List<Type>();
                claims[key] = list;
            }
            if (!list.Contains(t)) list.Add(t);
        }
    }
}
