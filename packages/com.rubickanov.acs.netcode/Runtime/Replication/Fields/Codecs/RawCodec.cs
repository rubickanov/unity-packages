using Unity.Netcode;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Default codec — raw <c>sizeof(T)</c> memcpy. Wire-byte-equivalent to the pre-codec
    /// <see cref="ReplicatedFieldBinding{T}"/> path, so existing tests and persisted snapshots
    /// continue to round-trip unchanged.
    /// </summary>
    [Preserve]
    internal sealed class RawCodec<T> : IFieldCodec<T> where T : unmanaged
    {
        public unsafe int Size => sizeof(T);

        public unsafe void Write(FastBufferWriter writer, in T value)
        {
            fixed (T* ptr = &value)
                writer.WriteBytesSafe((byte*)ptr, sizeof(T));
        }

        public unsafe T Read(FastBufferReader reader)
        {
            T value;
            reader.ReadBytesSafe((byte*)&value, sizeof(T));
            return value;
        }
    }
}
