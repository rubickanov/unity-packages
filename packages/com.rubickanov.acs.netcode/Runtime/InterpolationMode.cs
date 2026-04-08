namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// How a replicated field is interpolated on non-authority clients.
    /// </summary>
    public enum InterpolationMode
    {
        /// <summary>No interpolation — value is applied immediately.</summary>
        None,

        /// <summary>Linearly interpolate between received snapshots.</summary>
        Linear
    }
}
