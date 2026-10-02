using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Inspection and validation surface for the persistence pipeline. All entry points are
    /// add-only, side-effect-free (no cache pollution, no logging) and intended for bootstrap
    /// checks, editor tooling, and dev-build sanity dumps — not for hot paths.
    /// </summary>
    public static class PersistenceDebug
    {
        /// <summary>
        /// Fails fast if <paramref name="aspectType"/> has any <c>[PersistedState]</c> field
        /// that the scanner would reject (unsupported container, non-value/string element,
        /// enum without <c>[PersistedEnum]</c>, etc.). The thrown message contains the full
        /// list of offending fields so the caller sees every problem in one shot.
        /// </summary>
        public static void ValidateAspect(Type aspectType)
        {
            if (aspectType == null) throw new ArgumentNullException(nameof(aspectType));
            if (!typeof(IEntityAspect).IsAssignableFrom(aspectType))
                throw new ArgumentException(
                    $"[acs.persistence] ValidateAspect: '{aspectType.FullName}' does not implement IEntityAspect.",
                    nameof(aspectType));

            var errors = PersistenceScanner.CollectValidationErrors(aspectType);
            if (errors.Count == 0) return;

            var sb = new StringBuilder();
            sb.Append("[acs.persistence] ValidateAspect: '").Append(aspectType.FullName)
                .Append("' has ").Append(errors.Count).AppendLine(" invalid [PersistedState] field(s):");
            for (int i = 0; i < errors.Count; i++)
                sb.Append("  - ").AppendLine(errors[i]);
            throw new InvalidOperationException(sb.ToString());
        }

        /// <summary>Generic shortcut for <see cref="ValidateAspect(System.Type)"/>.</summary>
        public static void ValidateAspect<T>() where T : IEntityAspect => ValidateAspect(typeof(T));

        /// <summary>
        /// Scans <paramref name="assembly"/> (or every loaded assembly when null) for
        /// <see cref="IEntityAspect"/> implementations and returns the full list of scanner
        /// rejections as human-readable strings — one entry per offending field. An empty
        /// list means every aspect passed. Call at bootstrap to get a single go / no-go
        /// signal before any snapshot runs.
        /// </summary>
        public static IReadOnlyList<string> ValidateAllAspects(Assembly assembly = null)
        {
            var errors = new List<string>();
            var assemblies = assembly != null
                ? new[] { assembly }
                : AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
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

                    foreach (var error in PersistenceScanner.CollectValidationErrors(t))
                        errors.Add(error);
                }
            }

            return errors;
        }

        /// <summary>
        /// Dumps the registry's current reverse index — every key the registry will resolve,
        /// along with the target type, its <c>[PersistedVersion]</c>, and the list of
        /// <c>[PersistedAlias]</c> values pointing to it. One entry per (key → type) pair;
        /// the same type appears multiple times if it has <c>[PersistedKey]</c> and aliases.
        /// </summary>
        public static IReadOnlyList<PersistedKeyEntry> ListPersistedKeys()
        {
            var result = new List<PersistedKeyEntry>();
            foreach (var pair in PersistedKeyRegistry.EnumerateReverseIndex())
            {
                var t = pair.Value;
                var version = PersistedKeyRegistry.VersionOf(t);
                var aliases = AliasesOf(t);
                result.Add(new PersistedKeyEntry(pair.Key, t, version, aliases));
            }
            return result;
        }

        /// <summary>
        /// Re-walks loaded assemblies and reports every snapshot key claimed by more than
        /// one aspect type via <see cref="PersistedKeyAttribute"/> or
        /// <see cref="PersistedAliasAttribute"/>. Unlike the implicit collision-logging at
        /// first resolve, this method returns the complete set, including all claimants —
        /// use it in a bootstrap assertion to catch collisions in CI rather than in prod.
        /// </summary>
        public static IReadOnlyList<PersistedKeyCollision> FindKeyCollisions()
        {
            var raw = PersistedKeyRegistry.FindCollisions();
            var result = new List<PersistedKeyCollision>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
                result.Add(new PersistedKeyCollision(raw[i].Key, raw[i].Claimants));
            return result;
        }

        /// <summary>
        /// Rough snapshot of registry state — useful in dev-only overlays. Counts come from the
        /// eagerly-built key reverse index, not the lazy scanner cache:
        /// <see cref="PersistenceCacheStats.ScannedTypes"/> is the number of canonical aspect
        /// types registered in code (every concrete <c>IEntityAspect</c>, whether or not it has
        /// been snapshotted or carries any <c>[PersistedState]</c> field), and
        /// <see cref="PersistenceCacheStats.TotalFields"/> is the count of <c>[PersistedState]</c>-
        /// tagged fields across those types — including fields the scanner would later reject,
        /// since this does not run <c>TryClassify</c>.
        /// </summary>
        public static PersistenceCacheStats GetCacheStats()
        {
            int reverseIndexSize = 0;
            foreach (var _ in PersistedKeyRegistry.EnumerateReverseIndex()) reverseIndexSize++;

            // Hot Scanner.Cache is internal; derive scanned-types count by re-running validation
            // only on types that have at least one IEntityAspect implementation we've already
            // registered — cheap enough for a dev overlay, and avoids exposing the cache.
            int scannedTypes = 0;
            int totalFields = 0;
            foreach (var pair in PersistedKeyRegistry.EnumerateReverseIndex())
            {
                // Only count canonical (key == KeyOf) entries to avoid double-counting aliases.
                if (!string.Equals(pair.Key, PersistedKeyRegistry.KeyOf(pair.Value), StringComparison.Ordinal))
                    continue;
                scannedTypes++;
                totalFields += CountPersistedFields(pair.Value);
            }

            return new PersistenceCacheStats(scannedTypes, totalFields, reverseIndexSize);
        }

        /// <summary>
        /// Produces a human-readable multi-line dump of every <c>[PersistedState]</c> field on
        /// <paramref name="aspectType"/>, including the binding kind, value/key types, and
        /// enum mode when relevant. Meant for inspector previews and crash-report payloads.
        /// </summary>
        public static string DumpAspect(Type aspectType)
        {
            if (aspectType == null) throw new ArgumentNullException(nameof(aspectType));

            var sb = new StringBuilder();
            sb.Append("Aspect: ").Append(aspectType.FullName).AppendLine();
            sb.Append("Key:    ").Append(PersistedKeyRegistry.KeyOf(aspectType)).AppendLine();
            sb.Append("Version: ").Append(PersistedKeyRegistry.VersionOf(aspectType)).AppendLine();

            var aliases = AliasesOf(aspectType);
            if (aliases.Length > 0)
            {
                sb.Append("Aliases: ");
                for (int i = 0; i < aliases.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(aliases[i]);
                }
                sb.AppendLine();
            }

            sb.AppendLine("Fields:");
            var errors = PersistenceScanner.CollectValidationErrors(aspectType);
            for (int i = 0; i < errors.Count; i++)
                sb.Append("  ! ").AppendLine(errors[i]);

            foreach (var field in EnumeratePersistedFields(aspectType))
                sb.Append("  - ").AppendLine(DescribeField(field));

            return sb.ToString();
        }

        private static string[] AliasesOf(Type aspectType)
        {
            var attrs = aspectType.GetCustomAttributes<PersistedAliasAttribute>(inherit: false);
            var list = new List<string>();
            foreach (var a in attrs) list.Add(a.OldKey);
            return list.ToArray();
        }

        private static int CountPersistedFields(Type aspectType)
        {
            int count = 0;
            foreach (var _ in EnumeratePersistedFields(aspectType)) count++;
            return count;
        }

        private static IEnumerable<FieldInfo> EnumeratePersistedFields(Type aspectType)
        {
            var current = aspectType;
            while (current != null && current != typeof(object))
            {
                var fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].GetCustomAttribute<PersistedStateAttribute>() != null)
                        yield return fields[i];
                }
                current = current.BaseType;
            }
        }

        private static string DescribeField(FieldInfo field)
        {
            var enumAttr = field.GetCustomAttribute<PersistedEnumAttribute>(inherit: false);
            var mode = enumAttr != null ? $" [PersistedEnum({enumAttr.Mode})]" : string.Empty;
            return $"{field.Name} : {PrettyTypeName(field.FieldType)}{mode}";
        }

        private static string PrettyTypeName(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            var args = t.GetGenericArguments();
            var sb = new StringBuilder();
            var raw = t.Name;
            var tick = raw.IndexOf('`');
            sb.Append(tick > 0 ? raw.Substring(0, tick) : raw);
            sb.Append('<');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(PrettyTypeName(args[i]));
            }
            sb.Append('>');
            return sb.ToString();
        }
    }

    public readonly struct PersistedKeyEntry
    {
        public string Key { get; }
        public Type Type { get; }
        public int Version { get; }
        public string[] Aliases { get; }

        public PersistedKeyEntry(string key, Type type, int version, string[] aliases)
        {
            Key = key;
            Type = type;
            Version = version;
            Aliases = aliases;
        }
    }

    public readonly struct PersistedKeyCollision
    {
        public string Key { get; }
        public Type[] Claimants { get; }

        public PersistedKeyCollision(string key, Type[] claimants)
        {
            Key = key;
            Claimants = claimants;
        }
    }

    public readonly struct PersistenceCacheStats
    {
        public int ScannedTypes { get; }
        public int TotalFields { get; }
        public int ReverseIndexSize { get; }

        public PersistenceCacheStats(int scannedTypes, int totalFields, int reverseIndexSize)
        {
            ScannedTypes = scannedTypes;
            TotalFields = totalFields;
            ReverseIndexSize = reverseIndexSize;
        }
    }
}
