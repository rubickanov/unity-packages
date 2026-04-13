namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Where an <see cref="IEntityComponent"/> is allowed to run.
    /// Applied via <see cref="NetworkScopeAttribute"/> on the component class.
    /// </summary>
    public enum NetworkScope
    {
        /// <summary>Default. Runs on server, host and all clients — observers, bridges, VFX.</summary>
        Everywhere,

        /// <summary>Runs only on the server/host. Non-host clients have the component disabled.</summary>
        ServerOnly,

        /// <summary>Runs only on the client that owns the <c>NetworkObject</c>. Local input, camera, HUD.</summary>
        OwnerOnly
    }
}
