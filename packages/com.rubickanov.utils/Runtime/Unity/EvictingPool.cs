using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Utils
{
    /// <summary>
    /// LRU object pool that evicts the oldest active item when capacity is reached.
    /// Built on top of <see cref="ObjectPool{T}"/>.
    /// </summary>
    /// <remarks>
    /// When <see cref="Get"/> is called at full capacity, the oldest item is passed to the
    /// <c>onEvict</c> callback. If no callback is provided, the item is released back to the
    /// pool immediately. Use the callback for fade-out or other deferred release — call
    /// <see cref="Release"/> when done.
    /// </remarks>
    public class EvictingPool<T> : IDisposable where T : Component
    {
        private readonly ObjectPool<T> _pool;
        private readonly LinkedList<T> _active = new();
        private readonly int _maxActive;
        private readonly Action<T, Action<T>>? _onEvict;

        /// <summary>Number of items currently in active use (not counting items being evicted).</summary>
        public int ActiveCount => _active.Count;

        /// <summary>
        /// Creates a new evicting pool.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="maxActive">Maximum active items before eviction kicks in.</param>
        /// <param name="onEvict">
        /// Called when an item is evicted. Receives the item and a release callback.
        /// Call the release callback when the item is ready to return to the pool (e.g. after fade-out).
        /// If null, evicted items are released immediately.
        /// </param>
        /// <param name="evictBuffer">Extra pool capacity to hold items mid-eviction.</param>
        /// <param name="prewarm">Number of instances to pre-create.</param>
        public EvictingPool(
            T prefab,
            int maxActive,
            Action<T, Action<T>>? onEvict = null,
            int evictBuffer = 8,
            int prewarm = 0)
        {
            _maxActive = maxActive;
            _onEvict = onEvict;
            _pool = new ObjectPool<T>(prefab, prewarm, maxSize: maxActive + evictBuffer);
        }

        /// <summary>
        /// Gets an item from the pool, evicting the oldest if at capacity.
        /// </summary>
        public T Get(Vector3 position, Quaternion rotation)
        {
            if (_active.Count >= _maxActive)
                EvictOldest();

            var item = _pool.Get(position, rotation);
            _active.AddLast(item);
            return item;
        }

        /// <summary>
        /// Returns an item to the pool. Call this for normal release or from an eviction callback.
        /// </summary>
        public void Release(T item)
        {
            _active.Remove(item);
            _pool.Release(item);
        }

        /// <summary>Disposes the pool and all tracked items.</summary>
        public void Dispose()
        {
            _active.Clear();
            _pool.Dispose();
        }

        private void EvictOldest()
        {
            var oldest = _active.First!.Value;
            _active.RemoveFirst();

            if (_onEvict != null)
                _onEvict(oldest, _pool.Release);
            else
                _pool.Release(oldest);
        }
    }
}
