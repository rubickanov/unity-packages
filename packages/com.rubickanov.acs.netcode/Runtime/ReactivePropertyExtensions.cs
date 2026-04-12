using R3;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Extension methods for <see cref="ReactiveProperty{T}"/> that provide access to
    /// interpolated values for smooth rendering.
    /// </summary>
    [Preserve]
    public static class ReactivePropertyExtensions
    {
        /// <summary>
        /// Returns the interpolated (smoothed) value when network interpolation is active
        /// for this property, otherwise falls back to <see cref="ReactiveProperty{T}.Value"/>.
        /// Use in visual/rendering code (transform sync, animations).
        /// Game logic should read <see cref="ReactiveProperty{T}.Value"/> directly.
        /// </summary>
        public static T Smooth<T>(this ReactiveProperty<T> property) where T : unmanaged
        {
            return InterpolationRegistry.TryGetInterpolatedValue<T>(property, out T value)
                ? value
                : property.Value;
        }
    }
}
