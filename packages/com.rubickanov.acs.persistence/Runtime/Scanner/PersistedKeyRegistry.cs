using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// </summary>
    internal static class PersistedKeyRegistry
    {
        private static readonly Dictionary<Type, string> KeyByType = new();
        private static readonly Dictionary<Type, int> VersionByType = new();

        private static Dictionary<string, Type> _reverseIndex;
        private static bool _reverseIndexErrored; // coalesce "log once" on repeated misses after first build

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            KeyByType.Clear();
            VersionByType.Clear();
            _reverseIndex = null;
            _reverseIndexErrored = false;
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
            _reverseIndex = new Dictionary<string, Type>(StringComparer.Ordinal);
            _reverseIndexErrored = false;
        }

        /// <summary>
        /// Test-only hook. Routes through the same collision-logging path production uses.
        /// Requires <see cref="TestOnly_SeedEmptyReverseIndex"/> first.
        /// </summary>
        internal static void TestOnly_Register(string key, Type type, string role)
        {
            if (_reverseIndex == null)
                throw new InvalidOperationException("Call TestOnly_SeedEmptyReverseIndex first.");
            TryRegister(_reverseIndex, key, type, role);
        }

        public static string KeyOf(Type aspectType)
        {
            if (aspectType == null) throw new ArgumentNullException(nameof(aspectType));
            if (KeyByType.TryGetValue(aspectType, out var cached)) return cached;

            var attr = aspectType.GetCustomAttribute<PersistedKeyAttribute>(inherit: false);
            var key = attr?.Key ?? aspectType.FullName;
            KeyByType[aspectType] = key;
            return key;
        }

        public static int VersionOf(Type aspectType)
        {
            if (aspectType == null) throw new ArgumentNullException(nameof(aspectType));
            if (VersionByType.TryGetValue(aspectType, out var cached)) return cached;

            var attr = aspectType.GetCustomAttribute<PersistedVersionAttribute>(inherit: false);
            var version = attr?.Version ?? 0;
            VersionByType[aspectType] = version;
            return version;
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

            var index = GetOrBuildReverseIndex();
            if (index.TryGetValue(key, out aspectType)) return aspectType != null;

            // Fallback to Type.FullName scan for aspects that ship without attributes and
            // types that implement IEntityAspect only indirectly through a shipped assembly
            // loaded after our first sweep.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var candidate = asm.GetType(key, throwOnError: false, ignoreCase: false);
                if (candidate == null) continue;
                if (!typeof(IEntityAspect).IsAssignableFrom(candidate)) continue;
                aspectType = candidate;
                index[key] = candidate; // cache positive hits for next time
                return true;
            }

            index[key] = null; // cache negative lookups — repeated misses don't re-scan
            aspectType = null;
            return false;
        }

        private static Dictionary<string, Type> GetOrBuildReverseIndex()
        {
            if (_reverseIndex != null) return _reverseIndex;

            var index = new Dictionary<string, Type>(StringComparer.Ordinal);

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
                }
            }

            _reverseIndex = index;
            return index;
        }

        private static void TryRegister(Dictionary<string, Type> index, string key, Type type, string role)
        {
            if (index.TryGetValue(key, out var existing) && existing != null && existing != type)
            {
                if (!_reverseIndexErrored)
                {
                    Debug.LogError(
                        $"[acs.persistence] PersistedKeyRegistry: key '{key}' claimed by '{type.FullName}' via {role}, " +
                        $"but already registered to '{existing.FullName}'. The later entry is ignored — fix the collision.");
                    _reverseIndexErrored = true;
                }
                return;
            }

            index[key] = type;
        }
    }
}
