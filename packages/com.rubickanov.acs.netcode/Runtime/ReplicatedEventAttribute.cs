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
        /// <para>
        /// Use <see cref="Reliability.Reliable"/> for gameplay-critical events that MUST land
        /// and stay ordered — damage dealt, deaths, ability activations, inventory changes,
        /// quest state, chat. A dropped or reordered event here breaks game logic.
        /// </para>
        /// <para>
        /// Use <see cref="Reliability.Unreliable"/> for high-frequency cosmetic events where
        /// a missed packet is imperceptible — footsteps, muzzle flashes, hit particles, idle
        /// voice lines, breathing, cloth swishes. Lower latency (no head-of-line blocking
        /// behind stalled reliable packets) and cheaper under packet loss.
        /// </para>
        /// <para>
        /// Rule of thumb: if the player would notice a single missed instance, pick Reliable.
        /// If they would only notice *all* of them missing, pick Unreliable.
        /// </para>
        /// </summary>
        public Reliability Reliability { get; set; } = Reliability.Reliable;
    }
}
