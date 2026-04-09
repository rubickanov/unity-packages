using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    internal delegate T Lerp<T>(T a, T b, float t) where T : unmanaged;

    /// <summary>
    /// Registry of per-type linear interpolators used by <see cref="InterpolatedFieldBinding{T}"/>.
    /// Only unmanaged types with a meaningful "midpoint" are registered here.
    /// </summary>
    internal static class Interpolators
    {
        private static readonly Dictionary<Type, object> Lerpers = new()
        {
            [typeof(float)] = (Lerp<float>)((a, b, t) => Mathf.Lerp(a, b, t)),
            [typeof(double)] = (Lerp<double>)((a, b, t) => a + (b - a) * t),
            [typeof(Vector2)] = (Lerp<Vector2>)Vector2.Lerp,
            [typeof(Vector3)] = (Lerp<Vector3>)Vector3.Lerp,
            [typeof(Vector4)] = (Lerp<Vector4>)Vector4.Lerp,
            [typeof(Quaternion)] = (Lerp<Quaternion>)Quaternion.Slerp,
            [typeof(Color)] = (Lerp<Color>)Color.Lerp,
        };

        public static bool TryGet<T>(out Lerp<T> lerp) where T : unmanaged
        {
            if (Lerpers.TryGetValue(typeof(T), out var obj))
            {
                lerp = (Lerp<T>)obj;
                return true;
            }

            lerp = null!;
            return false;
        }

        public static bool TryGetRaw(Type type, out object lerp)
        {
            return Lerpers.TryGetValue(type, out lerp!);
        }
    }
}
