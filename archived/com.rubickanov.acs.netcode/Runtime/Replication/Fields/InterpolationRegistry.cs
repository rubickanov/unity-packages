using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
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
    /// Non-generic reset dispatcher for <see cref="InterpolationRegistry{T}"/>. Unity does not
    /// fire <see cref="RuntimeInitializeOnLoadMethodAttribute"/> on methods of generic classes,
    /// so each closed generic registers a clearer delegate via its static constructor and this
    /// class walks the list at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>.
    /// <para>
    /// <see cref="s_Clearers"/> itself is not cleared: closed generic static constructors fire
    /// once per domain lifetime, so with Domain Reload disabled the registrations persist across
    /// play sessions and each <see cref="ResetStatics"/> correctly clears every live
    /// <c>Bindings</c> dictionary. New closed generics introduced later register naturally on
    /// first touch.
    /// </para>
    /// </summary>
    internal static class InterpolationRegistry
    {
        private static readonly List<Action> s_Clearers = new();

        internal static void RegisterClearer(Action clearer) => s_Clearers.Add(clearer);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < s_Clearers.Count; i++)
                s_Clearers[i]();
        }
    }

    /// <summary>
    /// Maps <see cref="ReactiveProperty{T}"/> instances to their active
    /// <see cref="IInterpolatedBinding{T}"/>, enabling
    /// <see cref="ReactivePropertyExtensions.Smooth{T}"/> to retrieve the interpolated value
    /// without exposing internal binding types.
    /// <para>
    /// Per-closed-generic static storage (one dictionary per <c>T</c>) — keys are typed
    /// <see cref="ReactiveProperty{T}"/> so <see cref="Smooth"/> needs no runtime
    /// <c>is IInterpolatedBinding&lt;T&gt;</c> cast, and a wrong-<c>T</c> lookup is a
    /// compile error rather than a silent miss.
    /// </para>
    /// </summary>
    [Preserve]
    internal static class InterpolationRegistry<T> where T : unmanaged
    {
        private static readonly Dictionary<ReactiveProperty<T>, IInterpolatedBinding<T>> Bindings = new();

        // Register a Bindings.Clear() delegate with the non-generic dispatcher so
        // RuntimeInitializeOnLoadMethod (which Unity does not fire on generic classes)
        // can still reach this closed generic's static state.
        static InterpolationRegistry()
        {
            InterpolationRegistry.RegisterClearer(Bindings.Clear);
        }

        internal static void Register(ReactiveProperty<T> reactive, IInterpolatedBinding<T> binding)
        {
            // Double-register silently overwrites the prior binding — a real bug in test
            // teardown or ownership-transfer cleanup would go unnoticed until Smooth() started
            // returning values from a stale binding. Release builds strip Debug.Assert, so the
            // previous guard was invisible in shipping builds; use an unconditional error log
            // (runtime-observable) and keep the overwrite so legitimate teardown-then-register
            // cycles still recover gracefully.
            if (Bindings.ContainsKey(reactive))
            {
                Debug.LogError(
                    $"[InterpolationRegistry<{typeof(T).Name}>] double-register on the same ReactiveProperty — " +
                    $"previous binding silently overwritten. Likely a missed Unregister in binding teardown " +
                    $"or ownership-transfer cleanup.");
            }
            Bindings[reactive] = binding;
        }

        internal static void Unregister(ReactiveProperty<T> reactive)
        {
            Bindings.Remove(reactive);
        }

        internal static bool TryGetInterpolatedValue(ReactiveProperty<T> reactive, out T value)
        {
            if (Bindings.TryGetValue(reactive, out var binding))
            {
                value = binding.InterpolatedValue;
                return true;
            }

            value = default;
            return false;
        }
    }
}
