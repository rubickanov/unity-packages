using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>Half-float per component for <c>Vector3</c>. 12B → 6B.</summary>
    [Preserve]
    internal sealed class Vector3HalfCodec : IFieldCodec<Vector3>
    {
        public int Size => 6;

        public void Write(FastBufferWriter writer, in Vector3 value)
        {
            ushort hx = Mathf.FloatToHalf(value.x);
            ushort hy = Mathf.FloatToHalf(value.y);
            ushort hz = Mathf.FloatToHalf(value.z);
            writer.WriteValueSafe(hx);
            writer.WriteValueSafe(hy);
            writer.WriteValueSafe(hz);
        }

        public Vector3 Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort hx);
            reader.ReadValueSafe(out ushort hy);
            reader.ReadValueSafe(out ushort hz);
            return new Vector3(Mathf.HalfToFloat(hx), Mathf.HalfToFloat(hy), Mathf.HalfToFloat(hz));
        }
    }
}
