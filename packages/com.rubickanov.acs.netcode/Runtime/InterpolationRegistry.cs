using System.Collections.Generic;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Implemented by binding types that expose a per-frame smoothed view of a field's value.
    /// <see cref="ReactivePropertyExtensions.Smooth{T}"/> queries the registry via this interface,
    /// which lets multiple binding flavours (network-buffered, authority-side render-smoothed) plug
    /// in without expanding the registry's type-check ladder.
    /// </summary>
    internal interface IInterpolatedBinding<T> where T : unmanaged
    {
        T InterpolatedValue { get; }
    }

    /// <summary>
    /// Maps ReactiveProperty instances to their active <see cref="IInterpolatedBinding{T}"/>,
    /// enabling <see cref="ReactivePropertyExtensions.Smooth{T}"/> to retrieve the interpolated
    /// value without exposing internal binding types.
    /// </summary>
    [Preserve]
    internal static class InterpolationRegistry
    {
        private static readonly Dictionary<object, object> Bindings = new();

        internal static void Register(object reactiveProperty, object binding)
        {
            Bindings[reactiveProperty] = binding;
        }

        internal static void Unregister(object reactiveProperty)
        {
            Bindings.Remove(reactiveProperty);
        }

        internal static bool TryGetInterpolatedValue<T>(object reactiveProperty, out T value)
            where T : unmanaged
        {
            if (Bindings.TryGetValue(reactiveProperty, out var bindingObj)
                && bindingObj is IInterpolatedBinding<T> binding)
            {
                value = binding.InterpolatedValue;
                return true;
            }

            value = default;
            return false;
        }
    }
}
