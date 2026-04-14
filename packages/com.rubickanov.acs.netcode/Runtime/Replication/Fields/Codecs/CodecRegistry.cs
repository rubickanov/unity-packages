using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Resolves the <see cref="IFieldCodec{T}"/> singleton for a given
    /// (<see cref="QuantizationMode"/>, value type) pair. The factory passes the resolved codec
    /// (boxed as <c>object</c>) to <see cref="ReplicatedFieldBindingFactory"/>, which forwards it
    /// to the binding's generic ctor — the binding casts back to <c>IFieldCodec&lt;T&gt;</c>.
    /// </summary>
    [Preserve]
    internal static class CodecRegistry
    {
        // Quantizing codecs are stateless singletons keyed by (T, mode).
        private static readonly Dictionary<(Type, QuantizationMode), object> QuantizingCodecs = new();

        // Raw codecs are constructed lazily per T (covers user-defined unmanaged structs).
        private static readonly Dictionary<Type, object> RawCodecs = new();

        // Play-Mode-without-Domain-Reload safety (matches ReplicationScanner / Factory).
        // Static codecs are stateless so re-init is cheap.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            QuantizingCodecs.Clear();
            RawCodecs.Clear();
            RegisterBuiltIns();
        }

        static CodecRegistry()
        {
            RegisterBuiltIns();
        }

        private static void RegisterBuiltIns()
        {
            QuantizingCodecs[(typeof(float), QuantizationMode.HalfPrecision)] = new FloatHalfCodec();
            QuantizingCodecs[(typeof(Vector2), QuantizationMode.HalfPrecision)] = new Vector2HalfCodec();
            QuantizingCodecs[(typeof(Vector3), QuantizationMode.HalfPrecision)] = new Vector3HalfCodec();
            QuantizingCodecs[(typeof(Vector4), QuantizationMode.HalfPrecision)] = new Vector4HalfCodec();
            QuantizingCodecs[(typeof(Quaternion), QuantizationMode.SmallestThree)] = new QuaternionSmallestThreeCodec();
        }

        /// <summary>
        /// Returns the codec instance (boxed <c>IFieldCodec&lt;T&gt;</c>) for the pair, or
        /// throws if the combination is invalid (e.g. <see cref="QuantizationMode.HalfPrecision"/>
        /// on <c>int</c>). For <see cref="QuantizationMode.None"/> always returns
        /// <see cref="RawCodec{T}"/>.
        /// </summary>
        public static object Resolve(Type valueType, QuantizationMode mode)
        {
            if (mode == QuantizationMode.None)
                return GetOrCreateRaw(valueType);

            if (QuantizingCodecs.TryGetValue((valueType, mode), out var codec))
                return codec;

            throw new InvalidOperationException(
                $"[CodecRegistry] No codec for ({valueType.Name}, {mode}). " +
                $"Valid combinations: " +
                $"HalfPrecision on float/Vector2/Vector3/Vector4; " +
                $"SmallestThree on Quaternion. " +
                $"Use QuantizationMode.None for other unmanaged types.");
        }

        private static object GetOrCreateRaw(Type valueType)
        {
            if (RawCodecs.TryGetValue(valueType, out var cached))
                return cached;

            var codecType = typeof(RawCodec<>).MakeGenericType(valueType);
            // Parameterless ctor — same IL2CPP-safety story as ReplicatedFieldBindingFactory:
            // closed generic ctors are preserved by AotHints + [Preserve] on RawCodec<T>.
            var ctor = codecType.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException($"No parameterless ctor on {codecType}.");
            var codec = ctor.Invoke(null);
            RawCodecs[valueType] = codec;
            return codec;
        }

        /// <summary>
        /// Validates that a <see cref="QuantizationMode"/> is legal for a given value type.
        /// Used by <see cref="ReplicationScanner"/> to fail fast at scan time instead of at
        /// the first wire write. Returns <c>true</c> if valid, <c>false</c> otherwise.
        /// </summary>
        public static bool IsValid(Type valueType, QuantizationMode mode)
        {
            if (mode == QuantizationMode.None) return true;
            return QuantizingCodecs.ContainsKey((valueType, mode));
        }
    }
}
