using System;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Declares where an <see cref="IEntityComponent"/> class is allowed to run.
    /// Applied by <see cref="AspectReplicator"/> on <c>OnNetworkSpawn</c>: components whose
    /// scope does not match the current peer are disabled (<c>Behaviour.enabled = false</c>).
    /// For <see cref="NetworkScope.OwnerOnly"/>, the check is re-evaluated on ownership change.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class NetworkScopeAttribute : Attribute
    {
        public NetworkScope Scope { get; }

        public NetworkScopeAttribute(NetworkScope scope)
        {
            Scope = scope;
        }
    }
}
