using System;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Marks a <c>ReactiveProperty&lt;T&gt;</c> field on an aspect as part of the
    /// prediction snapshot. In step 6 this is a pure scan marker — the list of predicted
    /// fields is discovered and cached on <c>AspectReplicator</c> for step 7's rollback
    /// buffer. A predicted field must also be tagged with <see cref="ReplicatedStateAttribute"/>
    /// for its authoritative value to be replicated to non-authority peers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PredictedAttribute : Attribute
    {
    }
}
