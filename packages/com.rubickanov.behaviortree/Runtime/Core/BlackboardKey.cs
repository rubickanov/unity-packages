namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Typed key for <see cref="Blackboard"/> entries. Create as a static field and reuse.
    /// </summary>
    public sealed class BlackboardKey<T>
    {
        public string Name { get; }

        public BlackboardKey(string name)
        {
            Name = name;
        }

        public override string ToString() => Name;
    }
}