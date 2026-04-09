using System;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Marks a <c>Subject&lt;T&gt;</c> field on an aspect for automatic network event broadcast.
    /// Each <c>OnNext</c> call on the authority side is sent as an instant RPC to all other clients,
    /// where it is re-fired on the local <c>Subject</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReplicatedEventAttribute : Attribute
    {
        /// <summary>
        /// Who has write authority over this event. Default is <see cref="AuthorityMode.Server"/>.
        /// </summary>
        public AuthorityMode Authority { get; set; } = AuthorityMode.Server;

        /// <summary>
        /// Delivery reliability of the underlying RPC. Default is <see cref="Reliability.Reliable"/>.
        /// </summary>
        public Reliability Reliability { get; set; } = Reliability.Reliable;
    }
}
