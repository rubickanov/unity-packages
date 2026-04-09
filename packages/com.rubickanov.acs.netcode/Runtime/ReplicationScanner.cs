using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        public ReplicatedFieldInfo(FieldInfo field, Type valueType, AuthorityMode authority, InterpolationMode interpolation)
        {
            Field = field;
            ValueType = valueType;
            Authority = authority;
            Interpolation = interpolation;
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
                    var attr = fields[i].GetCustomAttribute<ReplicatedStateAttribute>();
                    if (attr == null) continue;

                    var fieldType = fields[i].FieldType;
                    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(ReactiveProperty<>))
                        continue;

                    var valueType = fieldType.GetGenericArguments()[0];

                    if (!IsUnmanagedType(valueType))
                    {
                        Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [ReplicatedState] but ReactiveProperty<{valueType.Name}> is not unmanaged. Only unmanaged types (primitives, enums, unmanaged structs) are supported.");
                        continue;
                    }

                    result.Add(new ReplicatedFieldInfo(
                        fields[i],
                        valueType,
                        attr.Authority,
                        attr.Interpolation));
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
                        continue;

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
