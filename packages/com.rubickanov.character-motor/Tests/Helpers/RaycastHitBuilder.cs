using System.Reflection;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    /// <summary>
    /// Builds stubbed <see cref="RaycastHit"/> instances for module tests that
    /// don't involve real physics. Unity exposes no public setters for
    /// <c>collider</c>, so reflection writes the private backing fields directly.
    /// </summary>
    internal static class RaycastHitBuilder
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo PointField = typeof(RaycastHit).GetField("m_Point", Flags)!;
        private static readonly FieldInfo NormalField = typeof(RaycastHit).GetField("m_Normal", Flags)!;
        private static readonly FieldInfo DistanceField = typeof(RaycastHit).GetField("m_Distance", Flags)!;
        private static readonly FieldInfo ColliderField = typeof(RaycastHit).GetField("m_Collider", Flags)!;

        public static RaycastHit Build(Vector3 point, Vector3 normal, float distance, Collider? collider = null)
        {
            object boxed = default(RaycastHit);

            PointField.SetValue(boxed, point);
            NormalField.SetValue(boxed, normal);
            DistanceField.SetValue(boxed, distance);

            if (collider != null)
            {
                // Unity stores m_Collider differently across versions:
                //   Legacy: Collider reference
                //   Intermediate: int instance ID
                //   Unity 6+: UnityEngine.EntityId struct (wraps instance ID)
                var fieldType = ColliderField.FieldType;
                if (fieldType == typeof(int))
                {
                    ColliderField.SetValue(boxed, collider.GetInstanceID());
                }
                else if (fieldType == typeof(Collider))
                {
                    ColliderField.SetValue(boxed, collider);
                }
                else
                {
                    // EntityId (or any other int-convertible struct): use op_Implicit(int).
                    var opImplicit = fieldType.GetMethod(
                        "op_Implicit",
                        BindingFlags.Static | BindingFlags.Public,
                        binder: null,
                        types: new[] { typeof(int) },
                        modifiers: null);
                    if (opImplicit == null)
                        throw new System.InvalidOperationException(
                            $"Cannot convert instance id to RaycastHit.m_Collider field type {fieldType.FullName}");
                    ColliderField.SetValue(boxed, opImplicit.Invoke(null, new object[] { collider.GetInstanceID() }));
                }
            }

            return (RaycastHit)boxed;
        }
    }
}
