namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Per-tick context passed to every node. Contains owner reference, blackboard, timing, and tick counter.
    /// </summary>
    public struct BTContext
    {
        public readonly object? Owner;
        public readonly Blackboard Blackboard;
        public float DeltaTime;
        public float Time;
        public uint Tick;

        public BTContext(object? owner, Blackboard blackboard, float deltaTime, float time, uint tick)
        {
            Owner = owner;
            Blackboard = blackboard;
            DeltaTime = deltaTime;
            Time = time;
            Tick = tick;
        }
    }
}