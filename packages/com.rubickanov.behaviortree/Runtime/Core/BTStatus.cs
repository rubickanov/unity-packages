namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Result of a single node tick.
    /// </summary>
    public enum BTStatus
    {
        Success,
        Failure,
        Running
    }
}