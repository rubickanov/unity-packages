using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rubickanov.Utils
{
    internal static class PoolRoot
    {
        private static Transform? _root;

        public static Transform Get()
        {
            if (_root == null)
                _root = new GameObject("[Pools]").transform;

            return _root;
        }
    }

    /// <summary>
    /// Generic GameObject pool. Instances are parented under a [Pools] root in the hierarchy.
    /// Supports prewarm, position/rotation placement on Get, and delayed release.
    /// </summary>
    public class ObjectPool<T> : IDisposable where T : Component
    {
        private readonly UnityEngine.Pool.ObjectPool<T> _pool;
        private readonly Transform _container;
        private readonly MonoBehaviour _coroutineRunner;
        private readonly HashSet<T> _pendingReleases = new();

        /// <summary>
        /// Creates a new pool for the given prefab.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="prewarm">Number of instances to pre-create.</param>
        /// <param name="maxSize">Maximum pooled instances before extras are destroyed.</param>
        public ObjectPool(T prefab, int prewarm = 0, int maxSize = 128)
        {
            _container = new GameObject($"Pool [{prefab.name}]").transform;
            _container.SetParent(PoolRoot.Get());
            _coroutineRunner = _container.gameObject.AddComponent<PoolCoroutineRunner>();

            _pool = new UnityEngine.Pool.ObjectPool<T>(
                createFunc: () =>
                {
                    var instance = Object.Instantiate(prefab, _container);
                    instance.gameObject.SetActive(false);
                    return instance;
                },
                actionOnGet: instance => instance.gameObject.SetActive(true),
                actionOnRelease: instance => instance.gameObject.SetActive(false),
                actionOnDestroy: instance =>
                {
                    if (instance != null) Object.Destroy(instance.gameObject);
                },
                defaultCapacity: Mathf.Max(16, prewarm),
                maxSize: maxSize);

            if (prewarm > 0)
            {
                var buffer = new T[prewarm];
                for (int i = 0; i < prewarm; i++)
                    buffer[i] = _pool.Get();

                for (int i = 0; i < prewarm; i++)
                    _pool.Release(buffer[i]);
            }
        }

        /// <summary>Gets an instance from the pool and places it at the given position and rotation.</summary>
        public T Get(Vector3 position, Quaternion rotation)
        {
            var instance = _pool.Get();
            instance.transform.SetPositionAndRotation(position, rotation);
            return instance;
        }

        /// <summary>Returns an instance to the pool immediately.</summary>
        public void Release(T instance) => _pool.Release(instance);

        /// <summary>Returns an instance to the pool after a delay in seconds.</summary>
        public void Release(T instance, float delay)
        {
            _pendingReleases.Add(instance);
            _coroutineRunner.StartCoroutine(DelayedRelease(instance, delay));
        }

        private IEnumerator DelayedRelease(T instance, float delay)
        {
            yield return new WaitForSeconds(delay);
            _pendingReleases.Remove(instance);
            _pool.Release(instance);
        }

        /// <summary>Disposes the pool, releasing all pending instances and destroying the container.</summary>
        public void Dispose()
        {
            if (_coroutineRunner != null)
                _coroutineRunner.StopAllCoroutines();

            foreach (var instance in _pendingReleases)
            {
                if (instance != null)
                    _pool.Release(instance);
            }

            _pendingReleases.Clear();
            _pool.Dispose();

            if (_container != null)
                Object.Destroy(_container.gameObject);
        }
    }

    internal class PoolCoroutineRunner : MonoBehaviour { }
}
