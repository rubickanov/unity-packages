using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObservableCollections;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    internal readonly struct ReplicatedFieldInfo
    {
        public readonly FieldInfo Field;
        public readonly Type ValueType;
        public readonly AuthorityMode Authority;
        public readonly InterpolationMode Interpolation;
        public readonly bool Predicted;
        public readonly QuantizationMode Quantization;

        public ReplicatedFieldInfo(FieldInfo field, Type valueType, AuthorityMode authority, InterpolationMode interpolation, bool predicted, QuantizationMode quantization)
        {
            Field = field;
            ValueType = valueType;
            Authority = authority;
            Interpolation = interpolation;
            Predicted = predicted;
            Quantization = quantization;
        }
    }

    internal readonly struct ReplicatedEventInfo
    {
        public readonly FieldInfo Field;
        public readonly Type ValueType;
        public readonly AuthorityMode Authority;
        public readonly Reliability Reliability;

        public ReplicatedEventInfo(FieldInfo field, Type valueType, AuthorityMode authority, Reliability reliability)
        {
            Field = field;
            ValueType = valueType;
            Authority = authority;
            Reliability = reliability;
        }
    }

    internal static class ReplicationScanner
    {
        private static readonly Dictionary<Type, ReplicatedFieldInfo[]> StateCache = new();
        private static readonly Dictionary<Type, ReplicatedEventInfo[]> EventCache = new();
        private static readonly Dictionary<Type, bool> UnmanagedCache = new();

        // Play-Mode-without-Domain-Reload safety (ISSUES.md #17 / TODO.md Batch 8).
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            StateCache.Clear();
            EventCache.Clear();
            UnmanagedCache.Clear();
        }

        private static bool ImplementsObservableCollection(Type fieldType)
        {
            var interfaces = fieldType.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                var iface = interfaces[i];
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IObservableCollection<>))
                    return true;
            }
            return false;
        }

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

        public static ReplicatedFieldInfo[] Scan(object aspect)
        {
            var type = aspect.GetType();
            if (StateCache.TryGetValue(type, out var cached))
                return cached;

            var fields = CollectReplicatedFields(type);
            StateCache[type] = fields;
            return fields;
        }

        public static ReplicatedEventInfo[] ScanEvents(object aspect)
        {
            var type = aspect.GetType();
            if (EventCache.TryGetValue(type, out var cached))
                return cached;

            var events = CollectReplicatedEvents(type);
            EventCache[type] = events;
            return events;
        }

        public static bool HasReplicatedFields(object aspect)
        {
            return Scan(aspect).Length > 0;
        }

        private static ReplicatedFieldInfo[] CollectReplicatedFields(Type aspectType)
        {
            var result = new List<ReplicatedFieldInfo>();
            var current = aspectType;

            while (current != null && current != typeof(object))
            {
                var fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

                for (int i = 0; i < fields.Length; i++)
                {
                    var attr = fields[i].GetCustomAttribute<ReplicatedAttribute>();
                    if (attr == null) continue;

                    var fieldType = fields[i].FieldType;
                    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(ReactiveProperty<>))
                    {
                        // Reactive collections (ObservableList/Dictionary/HashSet/RingBuffer) are
                        // a recognised runtime primitive but delta-replication for them is not
                        // implemented yet — see IDEAS.md "Reactive коллекции" / "ID-архитектура".
                        // Surface a targeted error so authors don't silently expect sync to happen.
                        if (ImplementsObservableCollection(fieldType))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] on an ObservableCollections type ({fieldType.Name}). Collection delta-replication is not implemented yet (see IDEAS.md). Subscribe locally; replicate mutations via a custom RPC until native support lands.");
                            continue;
                        }

                        Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] but is not a ReactiveProperty<T>.");
                        continue;
                    }

                    var valueType = fieldType.GetGenericArguments()[0];

                    if (!IsUnmanagedType(valueType))
                    {
                        Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] but ReactiveProperty<{valueType.Name}> is not unmanaged. Only unmanaged types (primitives, enums, unmanaged structs) are supported.");
                        continue;
                    }

                    // Single point of truth for the "Owner + Predicted is invalid" rule.
                    // The owner IS the authority for owner-auth fields, so there is no
                    // authoritative state to reconcile against. Leaving Predicted=true
                    // enabled triggers a real bug — the owner receives its own state
                    // batch back via the server relay (with owner-auth writes suppressed
                    // by SkipOwnerAuthIfLocallyWritten), which still dispatches to
                    // PredictionManager.OnServerStateApplied → replay. The replay re-runs
                    // Simulate for ticks that were already simulated in OnTick and never
                    // rolled back, so the owner accelerates by one Simulate pass per
                    // received batch. Clear the flag (field still replicates) so
                    // downstream PredictionScanner never sees it.
                    bool predicted = attr.Predicted;
                    if (predicted && attr.Authority == AuthorityMode.Owner)
                    {
                        Debug.LogWarning($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Predicted = true)] but Authority is Owner. Prediction and reconciliation are no-ops for owner-authoritative fields — the owner is already the source of truth. Dropping Predicted on this field.");
                        predicted = false;
                    }

                    // Fail-fast on invalid (T, QuantizationMode) at scan time so the developer
                    // sees the error before the first wire write rather than chasing a
                    // mid-tick InvalidOperationException from CodecRegistry.
                    var quantization = attr.Quantization;
                    if (!CodecRegistry.IsValid(valueType, quantization))
                    {
                        Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Quantization = {quantization})] which is not valid for type '{valueType.Name}'. Valid combinations: HalfPrecision on float/Vector2/Vector3/Vector4; SmallestThree on Quaternion. Field is skipped.");
                        continue;
                    }

                    result.Add(new ReplicatedFieldInfo(
                        fields[i],
                        valueType,
                        attr.Authority,
                        attr.Interpolation,
                        predicted,
                        quantization));
                }

                current = current.BaseType;
            }

            // Sort by name for stable bitmask ordering between server and client
            return result.OrderBy(f => f.Field.Name).ToArray();
        }

        private static ReplicatedEventInfo[] CollectReplicatedEvents(Type aspectType)
        {
            var result = new List<ReplicatedEventInfo>();
            var current = aspectType;

            while (current != null && current != typeof(object))
            {
                var fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

                for (int i = 0; i < fields.Length; i++)
                {
                    var attr = fields[i].GetCustomAttribute<ReplicatedEventAttribute>();
                    if (attr == null) continue;

                    var fieldType = fields[i].FieldType;
                    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(Subject<>))
                    {
                        Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [ReplicatedEvent] but is not a Subject<T>.");
                        continue;
                    }

                    var valueType = fieldType.GetGenericArguments()[0];

                    if (!IsUnmanagedType(valueType))
                    {
                        Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [ReplicatedEvent] but Subject<{valueType.Name}> is not unmanaged. Only unmanaged types (primitives, enums, unmanaged structs) are supported.");
                        continue;
                    }

                    result.Add(new ReplicatedEventInfo(
                        fields[i],
                        valueType,
                        attr.Authority,
                        attr.Reliability));
                }

                current = current.BaseType;
            }

            // Sort by name for stable event index ordering between server and client
            return result.OrderBy(f => f.Field.Name).ToArray();
        }
    }
}
