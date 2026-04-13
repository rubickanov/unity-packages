namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Delivery reliability for a replicated event RPC.
    /// </summary>
    public enum Reliability
    {
        /// <summary>Guaranteed delivery and ordering. Default.</summary>
        Reliable,

        /// <summary>Best-effort delivery, lower latency. Use for frequent cosmetic events where drops are acceptable.</summary>
        Unreliable
    }
}
