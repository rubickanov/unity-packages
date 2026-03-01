using System;
using System.Collections.Generic;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Type-keyed container for extensible input data.
    /// Modules define their own input structs and use them as keys.
    /// Core inputs (Move, Jump, Sprint, Crouch) stay on <see cref="MotorInput"/>;
    /// everything else goes through this container.
    /// </summary>
    public class InputExtensions
    {
        private readonly Dictionary<Type, object> _data = new();

        public void Set<T>(T value) where T : struct
        {
            _data[typeof(T)] = value;
        }

        public T Get<T>() where T : struct
        {
            return _data.TryGetValue(typeof(T), out var obj) ? (T)obj : default;
        }

        public bool TryGet<T>(out T value) where T : struct
        {
            if (_data.TryGetValue(typeof(T), out var obj))
            {
                value = (T)obj;
                return true;
            }

            value = default;
            return false;
        }

        public bool Has<T>() where T : struct
        {
            return _data.ContainsKey(typeof(T));
        }

        public bool Remove<T>() where T : struct
        {
            return _data.Remove(typeof(T));
        }

        public void Clear()
        {
            _data.Clear();
        }
    }
}
