using System;
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
    /// <remarks>
    /// No local cache: <see cref="ReplicationScanner.Scan"/> is already memoized
    /// per aspect type, so a second dictionary here would only save a count+copy
    /// on the cold spawn path.
    /// </remarks>
    internal static class PredictionScanner
    {
        public static PredictedFieldInfo[] Scan(object aspect)
        {
            var replicated = ReplicationScanner.Scan(aspect);

            int count = 0;
            for (int i = 0; i < replicated.Length; i++)
                if (replicated[i].Predicted) count++;

            if (count == 0) return Array.Empty<PredictedFieldInfo>();

            // ReplicationScanner already sorts by field name, so the order is preserved.
            var result = new PredictedFieldInfo[count];
            int w = 0;
            for (int i = 0; i < replicated.Length; i++)
            {
                if (!replicated[i].Predicted) continue;
                result[w++] = new PredictedFieldInfo(replicated[i].Field, replicated[i].ValueType);
            }
            return result;
        }

        public static bool HasPredictedFields(object aspect) => Scan(aspect).Length > 0;
    }
}
