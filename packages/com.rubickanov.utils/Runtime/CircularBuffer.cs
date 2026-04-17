#nullable enable
using System;

namespace Rubickanov.Utils
{
    /// <summary>
    /// Fixed-capacity ring buffer with index-based access. Indices are wrapped modulo <see cref="Capacity"/>,
    /// so any <see cref="uint"/> value maps to a valid slot. <c>uint</c>-underflow (e.g. <c>tick - 10u</c>
    /// when <c>tick &lt; 10</c>) wraps into a large value that still lands in a valid slot — ideal for
    /// look-back patterns in deterministic simulation.
    /// </summary>
    public sealed class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private readonly uint _capacity;

        /// <summary>Maximum number of items the buffer holds before overwriting.</summary>
        public uint Capacity => _capacity;

        /// <summary>Creates a new buffer with the given fixed capacity.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is zero.</exception>
        public CircularBuffer(uint capacity)
        {
            if (capacity == 0u)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            _capacity = capacity;
            _buffer = new T[capacity];
        }

        /// <summary>Writes <paramref name="item"/> into the slot at <c>index % Capacity</c>.</summary>
        public void Add(T item, uint index) => _buffer[index % _capacity] = item;

        /// <summary>Reads the item at <c>index % Capacity</c>.</summary>
        public T Get(uint index) => _buffer[index % _capacity];

        /// <summary>Resets every slot to <c>default(T)</c>. Does not allocate.</summary>
        public void Clear() => Array.Clear(_buffer, 0, (int)_capacity);
    }
}
