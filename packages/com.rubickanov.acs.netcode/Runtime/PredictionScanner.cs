using System;
using System.Collections.Generic;
using System.Reflection;

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
    /// Thin filter over <see cref="ReplicationScanner"/>: selects the subset of
    /// replicated fields that carry <c>Predicted = true</c>. All validation
    /// (ReactiveProperty shape, unmanaged value type, owner+predicted invariant)
    /// lives on <see cref="ReplicationScanner"/> now — by the time a field reaches
    /// this scanner its <c>Predicted</c> flag is already authoritative.
    /// </summary>
    internal static class PredictionScanner
    {
        private static readonly Dictionary<Type, PredictedFieldInfo[]> Cache = new();

        public static PredictedFieldInfo[] Scan(object aspect)
        {
            var type = aspect.GetType();
            if (Cache.TryGetValue(type, out var cached))
                return cached;

            var replicated = ReplicationScanner.Scan(aspect);
            var result = new List<PredictedFieldInfo>();
            for (int i = 0; i < replicated.Length; i++)
            {
                if (!replicated[i].Predicted) continue;
                result.Add(new PredictedFieldInfo(replicated[i].Field, replicated[i].ValueType));
            }

            // ReplicationScanner already sorts by field name, so the order is preserved.
            var array = result.ToArray();
            Cache[type] = array;
            return array;
        }

        public static bool HasPredictedFields(object aspect) => Scan(aspect).Length > 0;
    }
}
