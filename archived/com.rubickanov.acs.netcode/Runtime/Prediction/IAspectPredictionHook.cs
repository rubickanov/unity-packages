namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Non-generic view of <see cref="PredictionManager{TInput}"/> used by
    /// <see cref="EntityReplicator"/> to drive register / unregister / reconcile
    /// without holding a typed reference to a closed generic it cannot name.
    /// The implementation is resolved via reflection exactly once per entity
    /// (at register time) and stored on the replicator; the reconcile hot path
    /// then flows through a direct virtual call — no MethodInfo.Invoke, no
    /// per-call object[] allocation.
    /// </summary>
    internal interface IAspectPredictionHook
    {
        void Register(EntityReplicator replicator);
        void Unregister(EntityReplicator replicator);
        void OnServerStateApplied(EntityReplicator replicator, int serverTick);
    }
}
