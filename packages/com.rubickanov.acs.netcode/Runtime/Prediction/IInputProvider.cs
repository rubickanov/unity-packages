namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Supplies the owner's input for one tick. Implemented on a <c>MonoBehaviour</c>
    /// parented under the same entity as the <see cref="ISimulate{TInput}"/> components
    /// it drives; typically guarded with <c>[NetworkScope(NetworkScope.OwnerOnly)]</c>
    /// so it only ticks on the authority client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PredictionManager{TInput}"/> pulls input once per network tick per
    /// locally-owned predicted entity. If no provider is attached, the manager submits
    /// <c>default(TInput)</c> and logs a one-time warning.
    /// </para>
    /// </remarks>
    public interface IInputProvider<TInput>
        where TInput : unmanaged, IInputCommand
    {
        /// <summary>Return the input for the current tick.</summary>
        TInput Gather();
    }
}
