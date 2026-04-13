using System;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Marks a <c>ReactiveProperty&lt;T&gt;</c> field on an aspect for automatic network state replication.
    /// The field's value is synchronized from the authority side to all other clients via dirty-tick RPC.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReplicatedAttribute : Attribute
    {
        /// <summary>
        /// Who has write authority over this field. Default is <see cref="AuthorityMode.Server"/>.
        /// </summary>
        public AuthorityMode Authority { get; set; } = AuthorityMode.Server;

        /// <summary>
        /// How this field is interpolated on non-authority clients. Default is <see cref="InterpolationMode.None"/>.
        /// </summary>
        public InterpolationMode Interpolation { get; set; } = InterpolationMode.None;

        /// <summary>
        /// When <c>true</c>, this field participates in the prediction/reconciliation snapshot.
        /// The list of predicted fields is discovered at scan time and cached on
        /// <c>AspectReplicator</c> for the rollback buffer. Must be combined with
        /// <see cref="AuthorityMode.Server"/> — <c>Predicted</c> on an owner-auth field is
        /// a no-op (the owner is already the source of truth) and the scanner clears the
        /// flag with a warning.
        /// </summary>
        public bool Predicted { get; set; } = false;
    }
}
