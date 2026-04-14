using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Smallest-three packing for unit quaternions. 16B → 4B.
    /// <para>
    /// A unit quaternion's four components satisfy x²+y²+z²+w²=1. We store the index of the
    /// largest-magnitude component (2 bits, never sent) plus the other three components
    /// (10 signed bits each), then reconstruct the dropped one as
    /// <c>sqrt(1 - a² - b² - c²)</c>. Because q and -q describe the same rotation we sign-flip
    /// the whole quaternion so the dropped component is non-negative — the receiver can pick
    /// the positive root unambiguously.
    /// </para>
    /// <para>
    /// Layout of the packed <c>uint</c> (bit 31 = MSB):
    /// <code>
    /// [31..30] largestIndex (0..3)
    /// [29..20] component A  (10-bit signed via bias)
    /// [19..10] component B
    /// [ 9.. 0] component C
    /// </code>
    /// Components are scaled from [-1/√2, +1/√2] (their max possible range when "largest"
    /// is dropped) into the signed 10-bit interval [-511, 511], biased into [1, 1023] for
    /// unsigned packing. Reconstruction error ≈ 1/sqrt(2)/511 ≈ 0.0014 per component;
    /// the resulting quaternion's angular error stays under ~0.1° in typical cases.
    /// </para>
    /// <para>
    /// Input must be (approximately) a unit quaternion. Non-unit inputs decode to whatever
    /// quaternion the math produces; this codec does not normalize the input. If your field
    /// can hold non-unit quaternions, use <see cref="QuantizationMode.None"/> instead.
    /// </para>
    /// </summary>
    [Preserve]
    internal sealed class QuaternionSmallestThreeCodec : IFieldCodec<Quaternion>
    {
        // 10-bit signed range packed into unsigned bias-encoded form:
        //   [-511, 511] component values  →  [1, 1023] on-wire (after +Bias)
        // Bias = 512 keeps -512 reserved (unused) so 511 maps to 1023 cleanly.
        private const int Bits = 10;
        private const int Mask = (1 << Bits) - 1;     // 0x3FF
        private const int Bias = 1 << (Bits - 1);     // 512
        private const int MaxSigned = Bias - 1;       // 511

        // 1/sqrt(2) ≈ 0.70710678 — upper bound on each non-largest component when the
        // largest is dropped, so we scale this range into [-511, 511].
        private const float ComponentRange = 0.70710678f;
        private const float EncodeScale = MaxSigned / ComponentRange;
        private const float DecodeScale = ComponentRange / MaxSigned;

        public int Size => 4;

        public void Write(FastBufferWriter writer, in Quaternion value)
        {
            float x = value.x, y = value.y, z = value.z, w = value.w;
            float ax = x < 0f ? -x : x;
            float ay = y < 0f ? -y : y;
            float az = z < 0f ? -z : z;
            float aw = w < 0f ? -w : w;

            int largestIdx = 0;
            float largestAbs = ax;
            if (ay > largestAbs) { largestIdx = 1; largestAbs = ay; }
            if (az > largestAbs) { largestIdx = 2; largestAbs = az; }
            if (aw > largestAbs) { largestIdx = 3; }

            // Sign-flip the entire quaternion so the dropped (largest) component is
            // non-negative. q and -q encode the same rotation, so this is lossless.
            float largestSigned = largestIdx switch
            {
                0 => x,
                1 => y,
                2 => z,
                _ => w,
            };
            float sign = largestSigned < 0f ? -1f : 1f;

            float a, b, c;
            switch (largestIdx)
            {
                case 0: a = y * sign; b = z * sign; c = w * sign; break;
                case 1: a = x * sign; b = z * sign; c = w * sign; break;
                case 2: a = x * sign; b = y * sign; c = w * sign; break;
                default: a = x * sign; b = y * sign; c = z * sign; break;
            }

            uint packed =
                ((uint)largestIdx << 30) |
                ((uint)EncodeComponent(a) << 20) |
                ((uint)EncodeComponent(b) << 10) |
                (uint)EncodeComponent(c);

            writer.WriteValueSafe(packed);
        }

        public Quaternion Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out uint packed);

            int largestIdx = (int)((packed >> 30) & 0x3u);
            float a = DecodeComponent((int)((packed >> 20) & Mask));
            float b = DecodeComponent((int)((packed >> 10) & Mask));
            float c = DecodeComponent((int)(packed & Mask));

            float remaining = 1f - a * a - b * b - c * c;
            float largest = remaining > 0f ? Mathf.Sqrt(remaining) : 0f;

            return largestIdx switch
            {
                0 => new Quaternion(largest, a, b, c),
                1 => new Quaternion(a, largest, b, c),
                2 => new Quaternion(a, b, largest, c),
                _ => new Quaternion(a, b, c, largest),
            };
        }

        private static int EncodeComponent(float value)
        {
            int q = Mathf.RoundToInt(value * EncodeScale);
            if (q > MaxSigned) q = MaxSigned;
            else if (q < -MaxSigned) q = -MaxSigned;
            return (q + Bias) & Mask;
        }

        private static float DecodeComponent(int packed)
        {
            return (packed - Bias) * DecodeScale;
        }
    }
}
