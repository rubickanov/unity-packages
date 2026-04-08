using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using R3;

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

    internal static class ReplicationScanner
    {
        private static readonly Dictionary<Type, ReplicatedFieldInfo[]> Cache = new();

        public static ReplicatedFieldInfo[] Scan(object aspect)
        {
            var type = aspect.GetType();
            if (Cache.TryGetValue(type, out var cached))
                return cached;

            var fields = CollectReplicatedFields(type);
            Cache[type] = fields;
            return fields;
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
                        continue;

                    var valueType = fieldType.GetGenericArguments()[0];

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
    }
}
