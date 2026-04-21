using System;
using R3;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Syntactic sugar for the most common smooth-binding target: driving a
    /// <see cref="Transform"/> from a replicated <see cref="ReactiveProperty{T}"/>. Each
    /// helper is a two-line wrapper around <see cref="SmoothBinder.Bind{T}"/>; prefer the
    /// general <c>SmoothBinder.Bind</c> for anything outside of
    /// <see cref="Transform"/> writes.
    /// </summary>
    [Preserve]
    public static class SmoothTransformExtensions
    {
        public static IDisposable BindSmoothPosition(this Transform target, ReactiveProperty<Vector3> source)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return SmoothBinder.Bind(source, v => target.position = v);
        }

        public static IDisposable BindSmoothLocalPosition(this Transform target, ReactiveProperty<Vector3> source)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return SmoothBinder.Bind(source, v => target.localPosition = v);
        }

        public static IDisposable BindSmoothRotation(this Transform target, ReactiveProperty<Quaternion> source)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return SmoothBinder.Bind(source, v => target.rotation = v);
        }

        public static IDisposable BindSmoothLocalRotation(this Transform target, ReactiveProperty<Quaternion> source)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return SmoothBinder.Bind(source, v => target.localRotation = v);
        }

        public static IDisposable BindSmoothLocalScale(this Transform target, ReactiveProperty<Vector3> source)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return SmoothBinder.Bind(source, v => target.localScale = v);
        }
    }
}
