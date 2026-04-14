using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>Half-float per component for <c>Vector4</c>. 16B → 8B.</summary>
    [Preserve]
    internal sealed class Vector4HalfCodec : IFieldCodec<Vector4>
    {
        public int Size => 8;

        public void Write(FastBufferWriter writer, in Vector4 value)
        {
            ushort hx = Mathf.FloatToHalf(value.x);
            ushort hy = Mathf.FloatToHalf(value.y);
            ushort hz = Mathf.FloatToHalf(value.z);
            ushort hw = Mathf.FloatToHalf(value.w);
            writer.WriteValueSafe(hx);
            writer.WriteValueSafe(hy);
            writer.WriteValueSafe(hz);
            writer.WriteValueSafe(hw);
        }

        public Vector4 Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort hx);
            reader.ReadValueSafe(out ushort hy);
            reader.ReadValueSafe(out ushort hz);
            reader.ReadValueSafe(out ushort hw);
            return new Vector4(
                Mathf.HalfToFloat(hx),
                Mathf.HalfToFloat(hy),
                Mathf.HalfToFloat(hz),
                Mathf.HalfToFloat(hw));
        }
    }
}
