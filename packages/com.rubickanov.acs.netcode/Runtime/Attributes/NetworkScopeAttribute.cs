using System;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Declares where an <see cref="IEntityComponent"/> class is allowed to run.
    /// Applied by <see cref="EntityReplicator"/> on <c>OnNetworkSpawn</c>: components whose
    /// scope does not match the current peer are disabled (<c>Behaviour.enabled = false</c>).
    /// For <see cref="NetworkScope.OwnerOnly"/>, the check is re-evaluated on ownership change.
    /// </summary>
    /// <remarks>
    /// <b>IL2CPP note.</b> The scope is read via reflection
    /// (<c>Type.GetCustomAttribute&lt;NetworkScopeAttribute&gt;()</c>) on the component's concrete
    /// type. If IL2CPP strips the type (common for components referenced only from prefabs, not
    /// from any <c>typeof(...)</c> in code), the lookup returns <c>null</c> and the component
    /// silently falls back to <see cref="NetworkScope.Everywhere"/>. Preserve every type that
    /// carries this attribute in <c>Assets/link.xml</c> — see the IL2CPP section of the package
    /// README for a ready-to-paste snippet.
    /// </remarks>
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
