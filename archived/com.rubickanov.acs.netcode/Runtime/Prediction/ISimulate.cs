namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Component-level contract for prediction: implement on an <see cref="IEntityComponent"/>
    /// (or any <c>MonoBehaviour</c>) to be driven by <see cref="PredictionManager{TInput}"/>
    /// on each network tick. The same component runs on both the server (authority) and the
    /// owner client (local prediction). Non-owner pure clients consume the replicated result
    /// and do not invoke <c>Simulate</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <typeparamref name="TInput"/> constraint is <c>unmanaged</c> so the same unsafe
    /// byte-copy serialization used by <see cref="ReplicatedAttribute"/> fields also
    /// carries inputs to the server.
    /// </para>
    /// <para>
    /// An entity is expected to have a single <typeparamref name="TInput"/> across all of
    /// its <c>ISimulate</c> components — the input type is resolved from the first such
    /// component found in <c>EntityReplicator.OnNetworkSpawn</c>.
    /// </para>
    /// </remarks>
    public interface ISimulate<TInput>
        where TInput : unmanaged, IInputCommand
    {
        /// <summary>
        /// Apply one tick of simulation. Writes to <c>[Replicated(Predicted = true)]</c> fields
        /// will propagate through the normal replication path.
        /// </summary>
        /// <param name="input">Input for this tick (owner-gathered on the client, last-known on the server).</param>
        /// <param name="dt">Tick delta in seconds.</param>
        /// <remarks>
        /// <para>
        /// <b>MUST be idempotent with respect to predicted state.</b> Reconcile replays inputs from a
        /// restored snapshot, so <c>Simulate</c> will be invoked multiple times for the same tick
        /// whenever a server correction arrives. Running it N times with the same <paramref name="input"/>
        /// on the same starting state MUST produce the same ending state.
        /// </para>
        /// <para>
        /// Practical rules:
        /// <list type="bullet">
        /// <item>Read/write only the fields captured by the prediction snapshot — all
        /// <c>[Replicated(Predicted = true)]</c> fields on the owning aspect are captured automatically.
        /// State kept outside predicted fields (plain component fields, static caches, other
        /// <c>MonoBehaviour</c>s) is NOT rolled back during reconcile and will drift.</item>
        /// <item>No external side-effects (spawning objects, playing audio, raising events,
        /// mutating other entities). These would fire once per replay and can't be undone.</item>
        /// <item>No unseeded <c>Random</c> / wall-clock reads. If randomness is needed, derive
        /// it deterministically from inputs or a seed carried in predicted state.</item>
        /// </list>
        /// </para>
        /// </remarks>
        void Simulate(in TInput input, float dt);
    }
}
