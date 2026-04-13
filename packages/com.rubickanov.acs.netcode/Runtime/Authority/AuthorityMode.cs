namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Determines which side has write authority over a replicated field.
    /// </summary>
    public enum AuthorityMode
    {
        /// <summary>Server writes, clients receive.</summary>
        Server,

        /// <summary>Owner writes, server relays to other clients.</summary>
        Owner
    }
}
