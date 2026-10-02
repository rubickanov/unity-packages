namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Per-field wire-payload compression. Selected via
    /// <see cref="ReplicatedAttribute.Quantization"/>. Each mode is valid only for a
    /// specific subset of value types — invalid combinations throw at
    /// <c>ReplicationScanner.Scan</c> time.
    /// </summary>
    public enum QuantizationMode
    {
        /// <summary>
        /// No compression. Field is written/read as raw <c>sizeof(T)</c> bytes (current default).
        /// Valid for any unmanaged type.
        /// </summary>
        None = 0,

        /// <summary>
        /// IEEE-754 binary16 ("half-float") per component.
        /// <list type="bullet">
        ///   <item><c>float</c>:    4B → 2B</item>
        ///   <item><c>Vector2</c>:  8B → 4B</item>
        ///   <item><c>Vector3</c>: 12B → 6B</item>
        ///   <item><c>Vector4</c>: 16B → 8B</item>
        /// </list>
        /// Magnitude limit ±65504 with precision ≈0.001 near origin, ≈1.0 near limit.
        /// Suitable for positions, velocities, scalar stats. NaN/Inf preserved.
        /// </summary>
        HalfPrecision = 1,

        /// <summary>
        /// "Smallest-three" packing for <c>Quaternion</c>: 16B → 4B.
        /// Drops the largest-magnitude component, packs the index (2 bits) plus the three
        /// remaining components quantized to 10 signed bits each. Reconstruction error
        /// ≈0.001 rad — visually exact for orientation.
        /// </summary>
        SmallestThree = 2,
    }
}
