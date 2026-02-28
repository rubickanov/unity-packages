namespace Rubickanov.Utils
{
    /// <summary>Fixed-capacity ring buffer with index-based access.</summary>
    public class CircularBuffer<T>
    {
        private T[] _buffer;
        private readonly int _capacity;

        public CircularBuffer(int capacity)
        {
            _capacity = capacity;
            _buffer = new T[capacity];
        }

        public void Add(T item, int index) => _buffer[index % _capacity] = item;
        public T Get(int index) => _buffer[index % _capacity];
        public void Clear() => _buffer = new T[_capacity];
    }
}
