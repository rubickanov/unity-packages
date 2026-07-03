using System.Collections.Generic;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Type-safe key-value store for sharing data between nodes in a behavior tree.
    /// </summary>
    public class Blackboard
    {
        private readonly Dictionary<object, object> _data = new();

        public void Set<T>(BlackboardKey<T> key, T value) where T : notnull
        {
            _data[key] = value;
        }

        public T Get<T>(BlackboardKey<T> key)
        {
            if (!_data.TryGetValue(key, out var raw))
                throw new KeyNotFoundException($"Blackboard has no value for key '{key}'.");
            return (T)raw;
        }

        public bool TryGet<T>(BlackboardKey<T> key, out T? value)
        {
            if (_data.TryGetValue(key, out var raw))
            {
                value = (T)raw;
                return true;
            }
            value = default;
            return false;
        }

        public bool Has<T>(BlackboardKey<T> key)
        {
            return _data.ContainsKey(key);
        }

        public void Remove<T>(BlackboardKey<T> key)
        {
            _data.Remove(key);
        }
    }
}