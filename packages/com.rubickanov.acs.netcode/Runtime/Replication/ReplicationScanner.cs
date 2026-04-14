using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObservableCollections;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    // Scalar = ReactiveProperty<T>; Collection = ObservableList<T> / ObservableDictionary<K,V> /
    // ObservableHashSet<T> / ObservableFixedSizeRingBuffer<T>. Both kinds live in the same
    // ReplicatedFieldInfo array (and therefore share the per-entity dirty bitmask index
    // space) so scalar-only replicators keep the exact wire format they had before.
    internal enum ReplicatedFieldKind
    {
        Scalar,
        ObservableList,
        ObservableDictionary,
        ObservableHashSet,
        // Only ObservableFixedSizeRingBuffer<T> is supported — the plain unbounded
        // ObservableRingBuffer<T> is rejected at scan time (snapshot size would be
        // unbounded).
        ObservableRingBuffer,
    }

    internal readonly struct ReplicatedFieldInfo
    {
        public readonly FieldInfo Field;
        public readonly Type ValueType;
        // Populated only for ObservableDictionary kind — null otherwise. Kept as a
        // nullable Type instead of a discriminated union so the existing single-value
        // call sites (scalar / list) compile unchanged.
        public readonly Type KeyType;
        public readonly AuthorityMode Authority;
        public readonly InterpolationMode Interpolation;
        public readonly bool Predicted;
        public readonly QuantizationMode Quantization;
        public readonly ReplicatedFieldKind Kind;

        public ReplicatedFieldInfo(FieldInfo field, Type valueType, AuthorityMode authority, InterpolationMode interpolation, bool predicted, QuantizationMode quantization, ReplicatedFieldKind kind = ReplicatedFieldKind.Scalar, Type keyType = null)
        {
            Field = field;
            ValueType = valueType;
            KeyType = keyType;
            Authority = authority;
            Interpolation = interpolation;
            Predicted = predicted;
            Quantization = quantization;
            Kind = kind;
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

        // Phase 1–3 recognise ObservableList<T>, ObservableDictionary<K,V>,
        // ObservableHashSet<T>, and ObservableFixedSizeRingBuffer<T>. The plain
        // unbounded ObservableRingBuffer<T> is matched separately solely to emit a
        // targeted "use the fixed-size variant" diagnostic; it is NOT a supported
        // kind.
        private static bool TryMatchObservableList(Type fieldType, out Type elementType)
        {
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ObservableList<>))
            {
                elementType = fieldType.GetGenericArguments()[0];
                return true;
            }
            elementType = null;
            return false;
        }

        private static bool TryMatchObservableDictionary(Type fieldType, out Type keyType, out Type valueType)
        {
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ObservableDictionary<,>))
            {
                var args = fieldType.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
            keyType = null;
            valueType = null;
            return false;
        }

        private static bool TryMatchObservableHashSet(Type fieldType, out Type elementType)
        {
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ObservableHashSet<>))
            {
                elementType = fieldType.GetGenericArguments()[0];
                return true;
            }
            elementType = null;
            return false;
        }

        private static bool TryMatchObservableRingBuffer(Type fieldType, out Type elementType)
        {
            // Only the fixed-size variant is considered a match. The plain unbounded
            // ObservableRingBuffer<T> is recognised at the ImplementsObservableCollection
            // fallback and rejected with a targeted message.
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ObservableFixedSizeRingBuffer<>))
            {
                elementType = fieldType.GetGenericArguments()[0];
                return true;
            }
            elementType = null;
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

                    // ObservableDictionary<K,V> — delta-replicated map field. Key type
                    // allows unmanaged OR string (string handled via StringKeyCodec in
                    // the binding); value type follows the same rules as list elements
                    // (unmanaged OR EntityRef). Quantization applies to the VALUE only —
                    // keys always use raw / StringKeyCodec.
                    if (TryMatchObservableDictionary(fieldType, out var dictKeyType, out var dictValueType))
                    {
                        bool keyOk = IsUnmanagedType(dictKeyType) || dictKeyType == typeof(string);
                        if (!keyOk)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] but ObservableDictionary<{dictKeyType.Name},{dictValueType.Name}> key type is not unmanaged and not string. Supported key types: primitives, enums, unmanaged structs, string.");
                            continue;
                        }

                        if (!IsUnmanagedType(dictValueType) && dictValueType != typeof(EntityRef))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] but ObservableDictionary<{dictKeyType.Name},{dictValueType.Name}> value type is not unmanaged. Only unmanaged value types (primitives, enums, unmanaged structs) or EntityRef are supported.");
                            continue;
                        }

                        if (attr.Interpolation == InterpolationMode.Linear)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Interpolation = Linear)] on ObservableDictionary<{dictKeyType.Name},{dictValueType.Name}>. Interpolation is not supported for collection fields. Field is skipped.");
                            continue;
                        }
                        if (attr.Predicted)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Predicted = true)] on ObservableDictionary<{dictKeyType.Name},{dictValueType.Name}>. Prediction is not supported for collection fields. Field is skipped.");
                            continue;
                        }

                        var dictQuantization = attr.Quantization;
                        if (dictValueType == typeof(EntityRef) && dictQuantization != QuantizationMode.None)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' is ObservableDictionary<{dictKeyType.Name},EntityRef> and does not support [Replicated(Quantization = {dictQuantization})] — EntityRef is encoded as NetworkObjectId over the wire. Field is skipped.");
                            continue;
                        }
                        if (dictValueType != typeof(EntityRef) && !CodecRegistry.IsValid(dictValueType, dictQuantization))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Quantization = {dictQuantization})] which is not valid for ObservableDictionary value type '{dictValueType.Name}'. Valid combinations: HalfPrecision on float/Vector2/Vector3/Vector4; SmallestThree on Quaternion. Field is skipped.");
                            continue;
                        }

                        result.Add(new ReplicatedFieldInfo(
                            fields[i],
                            dictValueType,
                            attr.Authority,
                            InterpolationMode.None,
                            predicted: false,
                            dictQuantization,
                            ReplicatedFieldKind.ObservableDictionary,
                            dictKeyType));
                        continue;
                    }

                    // ObservableList<T> — delta-replicated collection field. Other
                    // IObservableCollection<> types (HashSet / RingBuffer) are recognised
                    // but unsupported in this phase; fall through to the targeted error
                    // below.
                    if (TryMatchObservableList(fieldType, out var listElementType))
                    {
                        if (!IsUnmanagedType(listElementType) && listElementType != typeof(EntityRef))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] but ObservableList<{listElementType.Name}> element type is not unmanaged. Only unmanaged element types (primitives, enums, unmanaged structs) or EntityRef are supported.");
                            continue;
                        }

                        // Interpolation and Prediction are meaningless for collections in
                        // MVP: there's no scalar to lerp, and the prediction pipeline
                        // operates on fixed-layout snapshots. Surface the mis-use at
                        // scan time rather than silently ignoring the flags.
                        if (attr.Interpolation == InterpolationMode.Linear)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Interpolation = Linear)] on ObservableList<{listElementType.Name}>. Interpolation is not supported for collection fields. Field is skipped.");
                            continue;
                        }
                        if (attr.Predicted)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Predicted = true)] on ObservableList<{listElementType.Name}>. Prediction is not supported for collection fields. Field is skipped.");
                            continue;
                        }

                        var collectionQuantization = attr.Quantization;
                        if (listElementType == typeof(EntityRef) && collectionQuantization != QuantizationMode.None)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' is ObservableList<EntityRef> and does not support [Replicated(Quantization = {collectionQuantization})] — EntityRef is encoded as NetworkObjectId over the wire. Field is skipped.");
                            continue;
                        }
                        if (listElementType != typeof(EntityRef) && !CodecRegistry.IsValid(listElementType, collectionQuantization))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Quantization = {collectionQuantization})] which is not valid for ObservableList element type '{listElementType.Name}'. Valid combinations: HalfPrecision on float/Vector2/Vector3/Vector4; SmallestThree on Quaternion. Field is skipped.");
                            continue;
                        }

                        result.Add(new ReplicatedFieldInfo(
                            fields[i],
                            listElementType,
                            attr.Authority,
                            InterpolationMode.None,
                            predicted: false,
                            collectionQuantization,
                            ReplicatedFieldKind.ObservableList));
                        continue;
                    }

                    // ObservableHashSet<T> — Phase 3. Same element-type rules as ObservableList<T>.
                    if (TryMatchObservableHashSet(fieldType, out var hashSetElementType))
                    {
                        if (!IsUnmanagedType(hashSetElementType) && hashSetElementType != typeof(EntityRef))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] but ObservableHashSet<{hashSetElementType.Name}> element type is not unmanaged. Only unmanaged element types (primitives, enums, unmanaged structs) or EntityRef are supported.");
                            continue;
                        }

                        if (attr.Interpolation == InterpolationMode.Linear)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Interpolation = Linear)] on ObservableHashSet<{hashSetElementType.Name}>. Interpolation is not supported for collection fields. Field is skipped.");
                            continue;
                        }
                        if (attr.Predicted)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Predicted = true)] on ObservableHashSet<{hashSetElementType.Name}>. Prediction is not supported for collection fields. Field is skipped.");
                            continue;
                        }

                        var hashSetQuantization = attr.Quantization;
                        if (hashSetElementType == typeof(EntityRef) && hashSetQuantization != QuantizationMode.None)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' is ObservableHashSet<EntityRef> and does not support [Replicated(Quantization = {hashSetQuantization})] — EntityRef is encoded as NetworkObjectId over the wire. Field is skipped.");
                            continue;
                        }
                        if (hashSetElementType != typeof(EntityRef) && !CodecRegistry.IsValid(hashSetElementType, hashSetQuantization))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Quantization = {hashSetQuantization})] which is not valid for ObservableHashSet element type '{hashSetElementType.Name}'. Valid combinations: HalfPrecision on float/Vector2/Vector3/Vector4; SmallestThree on Quaternion. Field is skipped.");
                            continue;
                        }

                        result.Add(new ReplicatedFieldInfo(
                            fields[i],
                            hashSetElementType,
                            attr.Authority,
                            InterpolationMode.None,
                            predicted: false,
                            hashSetQuantization,
                            ReplicatedFieldKind.ObservableHashSet));
                        continue;
                    }

                    // ObservableFixedSizeRingBuffer<T> — Phase 3. Same element-type rules as ObservableList<T>.
                    if (TryMatchObservableRingBuffer(fieldType, out var ringBufferElementType))
                    {
                        if (!IsUnmanagedType(ringBufferElementType) && ringBufferElementType != typeof(EntityRef))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] but ObservableFixedSizeRingBuffer<{ringBufferElementType.Name}> element type is not unmanaged. Only unmanaged element types (primitives, enums, unmanaged structs) or EntityRef are supported.");
                            continue;
                        }

                        if (attr.Interpolation == InterpolationMode.Linear)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Interpolation = Linear)] on ObservableFixedSizeRingBuffer<{ringBufferElementType.Name}>. Interpolation is not supported for collection fields. Field is skipped.");
                            continue;
                        }
                        if (attr.Predicted)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Predicted = true)] on ObservableFixedSizeRingBuffer<{ringBufferElementType.Name}>. Prediction is not supported for collection fields. Field is skipped.");
                            continue;
                        }

                        var ringBufferQuantization = attr.Quantization;
                        if (ringBufferElementType == typeof(EntityRef) && ringBufferQuantization != QuantizationMode.None)
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' is ObservableFixedSizeRingBuffer<EntityRef> and does not support [Replicated(Quantization = {ringBufferQuantization})] — EntityRef is encoded as NetworkObjectId over the wire. Field is skipped.");
                            continue;
                        }
                        if (ringBufferElementType != typeof(EntityRef) && !CodecRegistry.IsValid(ringBufferElementType, ringBufferQuantization))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated(Quantization = {ringBufferQuantization})] which is not valid for ObservableFixedSizeRingBuffer element type '{ringBufferElementType.Name}'. Valid combinations: HalfPrecision on float/Vector2/Vector3/Vector4; SmallestThree on Quaternion. Field is skipped.");
                            continue;
                        }

                        result.Add(new ReplicatedFieldInfo(
                            fields[i],
                            ringBufferElementType,
                            attr.Authority,
                            InterpolationMode.None,
                            predicted: false,
                            ringBufferQuantization,
                            ReplicatedFieldKind.ObservableRingBuffer));
                        continue;
                    }

                    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(ReactiveProperty<>))
                    {
                        // Plain unbounded ObservableRingBuffer<T> is intentionally not
                        // supported — snapshot size would be unbounded. Point the author
                        // at the fixed-size variant rather than hiding the rejection
                        // inside the generic "unsupported collection" message.
                        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ObservableRingBuffer<>))
                        {
                            var unboundedElement = fieldType.GetGenericArguments()[0];
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' uses plain ObservableRingBuffer<{unboundedElement.Name}>. Unbounded ring buffer replication is not supported (snapshot size is unbounded). Use ObservableFixedSizeRingBuffer<T> instead.");
                            continue;
                        }

                        // Any other unrecognised IObservableCollection<> implementation
                        // — defensive path for user subclasses. Cysharp's first-party
                        // collections are all handled above.
                        if (ImplementsObservableCollection(fieldType))
                        {
                            Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [Replicated] on an unrecognised ObservableCollections type ({fieldType.Name}). Supported: ObservableList<T>, ObservableDictionary<K,V>, ObservableHashSet<T>, ObservableFixedSizeRingBuffer<T>.");
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

                    // EntityRef uses EntityRefCodec (EntityId ↔ NetworkObjectId translation),
                    // not CodecRegistry — Quantization would silently be ignored, so catch
                    // the mis-use at scan time instead of letting it confuse the author.
                    if (valueType == typeof(EntityRef) && quantization != QuantizationMode.None)
                    {
                        Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' is EntityRef and does not support [Replicated(Quantization = {quantization})] — EntityRef is encoded as NetworkObjectId over the wire. Field is skipped.");
                        continue;
                    }

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
