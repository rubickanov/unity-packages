#nullable enable
using System;
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
            {
                var go = new GameObject("[Pools]");
                if (Application.isPlaying)
                    Object.DontDestroyOnLoad(go);
                _root = go.transform;
            }

            return _root;
        }
    }

    /// <summary>
    /// Generic GameObject pool. Instances are parented under a [Pools] root in the hierarchy.
    /// Supports prewarm, position/rotation placement on Get, delayed release, user callbacks,
    /// active tracking, ReleaseAll, and statistics.
    /// </summary>
    public sealed class ObjectPool<T> : IDisposable where T : Component
    {
        private readonly UnityEngine.Pool.ObjectPool<T> _pool;
        private readonly Transform _container;
        private readonly PoolTimerRunner _timerRunner;
        private readonly HashSet<T> _active = new();
        private readonly Action<T>? _onGet;
        private readonly Action<T>? _onRelease;
        private bool _disposed;

        /// <summary>Number of instances currently in active use.</summary>
        public int ActiveCount => _active.Count;

        /// <summary>Number of instances currently sitting in the pool (inactive).</summary>
        public int PooledCount => _pool.CountInactive;

        /// <summary>Total instances created by this pool (active + pooled).</summary>
        public int TotalCreated => _active.Count + _pool.CountInactive;

        /// <summary>
        /// Creates a new pool for the given prefab.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="prewarm">Number of instances to pre-create.</param>
        /// <param name="maxSize">Maximum pooled instances before extras are destroyed.</param>
        /// <param name="onGet">Called after an instance is retrieved from the pool (already active).</param>
        /// <param name="onRelease">Called before an instance is returned to the pool (still active).</param>
        /// <param name="parent">Optional parent transform. If null, uses the global [Pools] root.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="prefab"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="prewarm"/> is negative or <paramref name="maxSize"/> is not positive.</exception>
        public ObjectPool(
            T prefab,
            int prewarm = 0,
            int maxSize = 128,
            Action<T>? onGet = null,
            Action<T>? onRelease = null,
            Transform? parent = null)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));
            if (prewarm < 0)
                throw new ArgumentOutOfRangeException(nameof(prewarm), "Prewarm must be non-negative.");
            if (maxSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxSize), "MaxSize must be positive.");

            _onGet = onGet;
            _onRelease = onRelease;

            _container = new GameObject($"Pool [{prefab.name}]").transform;
            _container.SetParent(parent != null ? parent : PoolRoot.Get());

            _timerRunner = _container.gameObject.AddComponent<PoolTimerRunner>();
            _timerRunner.Initialize(c => Release((T)c));

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
                    if (instance != null) DestroySafe(instance.gameObject);
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

        /// <summary>Gets an instance from the pool without changing its transform.</summary>
        public T Get()
        {
            ThrowIfDisposed();
            var instance = _pool.Get();
            _active.Add(instance);
            _onGet?.Invoke(instance);
            return instance;
        }

        /// <summary>Gets an instance from the pool and places it at the given position and rotation.</summary>
        public T Get(Vector3 position, Quaternion rotation)
        {
            ThrowIfDisposed();
            var instance = _pool.Get();
            instance.transform.SetPositionAndRotation(position, rotation);
            _active.Add(instance);
            _onGet?.Invoke(instance);
            return instance;
        }

        /// <summary>Returns an instance to the pool immediately. Safe to call multiple times.</summary>
        public void Release(T instance)
        {
            ThrowIfDisposed();
            if (!_active.Remove(instance)) return;
            _timerRunner.Cancel(instance);
            _onRelease?.Invoke(instance);
            _pool.Release(instance);
        }

        /// <summary>Returns an instance to the pool after a delay in seconds.</summary>
        public void Release(T instance, float delay)
        {
            ThrowIfDisposed();
            _timerRunner.Schedule(instance, delay);
        }

        /// <summary>Returns all active instances to the pool, cancelling any pending delayed releases.</summary>
        public void ReleaseAll()
        {
            ThrowIfDisposed();
            _timerRunner.CancelAll();

            foreach (var instance in _active)
            {
                if (instance != null)
                {
                    _onRelease?.Invoke(instance);
                    _pool.Release(instance);
                }
            }

            _active.Clear();
        }

        /// <summary>Disposes the pool, returning all active instances and destroying the container.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _timerRunner.CancelAll();

            foreach (var instance in _active)
            {
                if (instance != null)
                {
                    _onRelease?.Invoke(instance);
                    _pool.Release(instance);
                }
            }

            _active.Clear();
            _pool.Dispose();

            if (_container != null)
                DestroySafe(_container.gameObject);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ObjectPool<T>));
        }

        private static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }
    }

    internal class PoolTimerRunner : MonoBehaviour
    {
        private struct PendingRelease
        {
            public Component Instance;
            public float ReleaseTime;
        }

        private readonly List<PendingRelease> _pending = new();
        private Action<Component>? _releaseCallback;

        public void Initialize(Action<Component> releaseCallback)
            => _releaseCallback = releaseCallback;

        public void Schedule(Component instance, float delay)
        {
            float releaseTime = Time.time + delay;
            for (int i = 0; i < _pending.Count; i++)
            {
                if (ReferenceEquals(_pending[i].Instance, instance))
                {
                    _pending[i] = new PendingRelease { Instance = instance, ReleaseTime = releaseTime };
                    return;
                }
            }

            _pending.Add(new PendingRelease
            {
                Instance = instance,
                ReleaseTime = releaseTime
            });
        }

        public void CancelAll() => _pending.Clear();

        public bool Cancel(Component instance)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (ReferenceEquals(_pending[i].Instance, instance))
                {
                    _pending[i] = _pending[^1];
                    _pending.RemoveAt(_pending.Count - 1);
                    return true;
                }
            }

            return false;
        }

        private void Update()
        {
            float time = Time.time;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (time >= _pending[i].ReleaseTime)
                {
                    var instance = _pending[i].Instance;
                    _pending[i] = _pending[^1];
                    _pending.RemoveAt(_pending.Count - 1);

                    if (instance != null)
                        _releaseCallback?.Invoke(instance);
                }
            }
        }
    }
}
