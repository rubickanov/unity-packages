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
    /// component found in <c>AspectReplicator.OnNetworkSpawn</c>.
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
        void Simulate(in TInput input, float dt);
    }
}
