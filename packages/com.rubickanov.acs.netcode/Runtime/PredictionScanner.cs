using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    internal readonly struct PredictedFieldInfo
    {
        public readonly FieldInfo Field;
        public readonly Type ValueType;

        public PredictedFieldInfo(FieldInfo field, Type valueType)
        {
            Field = field;
            ValueType = valueType;
        }
    }

    /// <summary>
    /// Collects fields marked with <see cref="PredictedAttribute"/> on an aspect type.
    /// Parallel to <see cref="ReplicationScanner"/>: the same reflection walk, the same
    /// per-type cache, the same stable sort by field name so the snapshot index of each
    /// predicted field is deterministic between host and client.
    /// </summary>
    internal static class PredictionScanner
    {
        private static readonly Dictionary<Type, PredictedFieldInfo[]> Cache = new();

        public static PredictedFieldInfo[] Scan(object aspect)
        {
            var type = aspect.GetType();
            if (Cache.TryGetValue(type, out var cached))
                return cached;

            var fields = Collect(type);
            Cache[type] = fields;
            return fields;
        }

        public static bool HasPredictedFields(object aspect) => Scan(aspect).Length > 0;

        private static PredictedFieldInfo[] Collect(Type aspectType)
        {
            var result = new List<PredictedFieldInfo>();
            var current = aspectType;

            while (current != null && current != typeof(object))
            {
                var fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

                for (int i = 0; i < fields.Length; i++)
                {
                    var attr = fields[i].GetCustomAttribute<PredictedAttribute>();
                    if (attr == null) continue;

                    var fieldType = fields[i].FieldType;
                    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(ReactiveProperty<>))
                    {
                        Debug.LogError($"[PredictionScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Predicted] but is not a ReactiveProperty<T>.");
                        continue;
                    }

                    var valueType = fieldType.GetGenericArguments()[0];

                    if (!IsUnmanagedType(valueType))
                    {
                        Debug.LogError($"[PredictionScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Predicted] but ReactiveProperty<{valueType.Name}> is not unmanaged. Only unmanaged types (primitives, enums, unmanaged structs) are supported.");
                        continue;
                    }

                    // [Predicted] on an owner-auth field is a no-op by intent: the owner IS
                    // the authority, so there is no authoritative state to reconcile against.
                    // Worse, leaving it enabled triggers a real bug — the owner receives its
                    // own state batch back via the server relay (with owner-auth writes
                    // suppressed by SkipOwnerAuthIfLocallyWritten), which still calls
                    // NotifyServerStateApplied → OnServerStateApplied → replay loop. The
                    // replay re-runs Simulate for ticks that were already simulated in OnTick
                    // and were never rolled back (ApplyStateBuffer skipped the owner-auth
                    // field), so the owner accelerates by one Simulate pass per received
                    // batch. Drop the field from the scan result so AspectReplicator never
                    // adds it to _predictedBindingIndices; PredictedPayloadSize then reflects
                    // only the server-auth predicted fields, and CaptureSnapshot/reconcile
                    // no-op naturally when everything predicted is owner-auth.
                    var replicated = fields[i].GetCustomAttribute<ReplicatedStateAttribute>();
                    if (replicated != null && replicated.Authority == AuthorityMode.Owner)
                    {
                        Debug.LogWarning($"[PredictionScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Predicted] but its [ReplicatedState] Authority is Owner. Prediction and reconciliation are no-ops for owner-authoritative fields — the owner is already the source of truth. Dropping [Predicted] on this field.");
                        continue;
                    }

                    result.Add(new PredictedFieldInfo(fields[i], valueType));
                }

                current = current.BaseType;
            }

            // Sort by name for stable snapshot ordering between peers.
            return result.OrderBy(f => f.Field.Name).ToArray();
        }

        // Same check as ReplicationScanner — kept local so the two scanners remain
        // independent and a future managed-serialization path for replicated fields
        // doesn't leak into predicted-field validation.
        private static readonly Dictionary<Type, bool> UnmanagedCache = new();
        private static bool IsUnmanagedType(Type type)
        {
            if (UnmanagedCache.TryGetValue(type, out var cached)) return cached;
            bool result = type.IsPrimitive
                          || type.IsEnum
                          || (type.IsValueType
                              && !type.IsGenericType
                              && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                  .All(f => IsUnmanagedType(f.FieldType)));
            UnmanagedCache[type] = result;
            return result;
        }
    }
}
