namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Marker for the per-game input struct driven through the prediction pipeline.
    /// Implementations must be <c>unmanaged</c> so the shared unsafe byte-copy serialization
    /// path used by replication can also move inputs across the wire. A single input type
    /// per game is assumed — an entity discovers its input type through the <see cref="ISimulate{TInput}"/>
    /// components attached to it.
    /// </summary>
    public interface IInputCommand
    {
    }
}
