using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Per-field write/read strategy. Allows <see cref="ReplicatedFieldBinding{T}"/> to switch
    /// between raw memcpy and lossy quantization (half-float, smallest-three, ...) without
    /// duplicating the binding class for each compression mode. Codecs are stateless singletons
    /// resolved by <see cref="CodecRegistry"/> from
    /// <see cref="QuantizationMode"/> + value type.
    /// </summary>
    /// <typeparam name="T">Replicated value type. Constrained to <c>unmanaged</c> by the
    /// surrounding binding system; codecs can rely on that without re-asserting it.</typeparam>
    internal interface IFieldCodec<T> where T : unmanaged
    {
        /// <summary>
        /// Number of bytes written by <see cref="Write"/> per call. Fixed per codec —
        /// the dirty mask + payloadBytes header at the batch level absorb variable totals,
        /// not the per-field size.
        /// </summary>
        int Size { get; }

        void Write(FastBufferWriter writer, in T value);

        T Read(FastBufferReader reader);
    }
}
