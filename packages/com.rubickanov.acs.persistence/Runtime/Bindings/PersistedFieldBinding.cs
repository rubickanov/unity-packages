namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Abstracts one <c>[PersistedState]</c> field of an aspect instance. Snapshot
    /// reads via <see cref="ReadValue"/>, Restore writes via <see cref="WriteValue"/>.
    /// Unlike the netcode binding there is no dirty-tracking, no codec, and no
    /// suppress flag — Restore is meant to look like a normal write so downstream
    /// subscribers (UI, rules, netcode replication) fire as they would at runtime.
    /// </summary>
    internal abstract class PersistedFieldBinding
    {
        public abstract object ReadValue();
        public abstract void WriteValue(object value);
    }
}
