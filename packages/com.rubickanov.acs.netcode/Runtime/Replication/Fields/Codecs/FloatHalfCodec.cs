using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// IEEE-754 binary16 codec for <c>float</c>. 4B → 2B.
    /// Range ±65504, precision degrades with magnitude (≈0.001 near 1.0, ≈1.0 near 65504).
    /// NaN/Inf/sub-normals preserved by Unity's <see cref="Mathf.FloatToHalf"/>.
    /// </summary>
    [Preserve]
    internal sealed class FloatHalfCodec : IFieldCodec<float>
    {
        public int Size => 2;

        public void Write(FastBufferWriter writer, in float value)
        {
            ushort half = Mathf.FloatToHalf(value);
            writer.WriteValueSafe(half);
        }

        public float Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort half);
            return Mathf.HalfToFloat(half);
        }
    }
}
