using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>Half-float per component for <c>Vector2</c>. 8B → 4B.</summary>
    [Preserve]
    internal sealed class Vector2HalfCodec : IFieldCodec<Vector2>
    {
        public int Size => 4;

        public void Write(FastBufferWriter writer, in Vector2 value)
        {
            ushort hx = Mathf.FloatToHalf(value.x);
            ushort hy = Mathf.FloatToHalf(value.y);
            writer.WriteValueSafe(hx);
            writer.WriteValueSafe(hy);
        }

        public Vector2 Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort hx);
            reader.ReadValueSafe(out ushort hy);
            return new Vector2(Mathf.HalfToFloat(hx), Mathf.HalfToFloat(hy));
        }
    }
}
