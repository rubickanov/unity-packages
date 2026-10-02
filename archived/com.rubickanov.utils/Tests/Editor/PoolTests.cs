using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.Utils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.Utils.Tests
{
    public class ObjectPoolTests
    {
        private BoxCollider CreatePrefab(string name = "prefab")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            return go.AddComponent<BoxCollider>();
        }

        private static void DestroyImmediate(Component c)
        {
            if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        [Test]
        public void Constructor_NullPrefab_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new ObjectPool<BoxCollider>(null!));
        }

        [Test]
        public void Constructor_NegativePrewarm_ThrowsArgumentOutOfRange()
        {
            var prefab = CreatePrefab();

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ObjectPool<BoxCollider>(prefab, prewarm: -1));

            DestroyImmediate(prefab);
        }

        [Test]
        public void Constructor_ZeroMaxSize_ThrowsArgumentOutOfRange()
        {
            var prefab = CreatePrefab();

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ObjectPool<BoxCollider>(prefab, maxSize: 0));

            DestroyImmediate(prefab);
        }

        [Test]
        public void Get_ReturnsActiveInstance()
        {
            var prefab = CreatePrefab();
            var pool = new ObjectPool<BoxCollider>(prefab);

            var instance = pool.Get();

            Assert.IsTrue(instance.gameObject.activeSelf);
            Assert.AreEqual(1, pool.ActiveCount);

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void Release_Immediate_ReturnsInstanceToPool()
        {
            var prefab = CreatePrefab();
            var pool = new ObjectPool<BoxCollider>(prefab);

            var instance = pool.Get();
            pool.Release(instance);

            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(1, pool.PooledCount);
            Assert.IsFalse(instance.gameObject.activeSelf);

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void Release_ImmediateAfterDelayed_CancelsPendingTimer()
        {
            // C3 regression: an immediate Release must cancel a pending delayed release of the
            // same instance so the timer cannot return a re-acquired instance to the pool.
            var prefab = CreatePrefab();
            var pool = new ObjectPool<BoxCollider>(prefab);

            var inst = pool.Get();
            pool.Release(inst, delay: 10f);

            var pending = GetPendingList(pool);
            Assert.AreEqual(1, pending.Count, "delayed release should be scheduled");

            pool.Release(inst);

            Assert.AreEqual(0, pending.Count, "immediate Release must cancel the pending entry");

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void Release_DelayedTwice_DeduplicatesByInstance()
        {
            // m8 regression: a second Release(inst, delay) must not add a duplicate entry.
            var prefab = CreatePrefab();
            var pool = new ObjectPool<BoxCollider>(prefab);

            var inst = pool.Get();
            pool.Release(inst, delay: 1f);
            pool.Release(inst, delay: 2f);

            var pending = GetPendingList(pool);
            Assert.AreEqual(1, pending.Count, "pending list must contain a single entry");

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            // M4 regression: a second Dispose must be idempotent.
            var prefab = CreatePrefab();
            var pool = new ObjectPool<BoxCollider>(prefab);

            pool.Dispose();

            Assert.DoesNotThrow(() => pool.Dispose());

            DestroyImmediate(prefab);
        }

        [Test]
        public void Get_AfterDispose_ThrowsObjectDisposed()
        {
            // M4 regression: public methods must throw ObjectDisposedException after Dispose.
            var prefab = CreatePrefab();
            var pool = new ObjectPool<BoxCollider>(prefab);
            pool.Dispose();

            Assert.Throws<ObjectDisposedException>(() => pool.Get());

            DestroyImmediate(prefab);
        }

        [Test]
        public void Release_AfterDispose_ThrowsObjectDisposed()
        {
            var prefab = CreatePrefab();
            var pool = new ObjectPool<BoxCollider>(prefab);
            pool.Dispose();
            var stray = CreatePrefab("stray");

            Assert.Throws<ObjectDisposedException>(() => pool.Release(stray));

            DestroyImmediate(prefab);
            DestroyImmediate(stray);
        }

        private static IList GetPendingList(object pool)
        {
            var timerField = pool.GetType().GetField("_timerRunner", BindingFlags.NonPublic | BindingFlags.Instance);
            var timerRunner = timerField!.GetValue(pool);
            var pendingField = timerRunner!.GetType().GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance);
            return (IList)pendingField!.GetValue(timerRunner)!;
        }
    }

    public class EvictingPoolTests
    {
        private BoxCollider CreatePrefab(string name = "evictPrefab")
        {
            var go = new GameObject(name);
            go.SetActive(false);
            return go.AddComponent<BoxCollider>();
        }

        private static void DestroyImmediate(Component c)
        {
            if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        [Test]
        public void Constructor_NonPositiveMaxActive_ThrowsArgumentOutOfRange()
        {
            var prefab = CreatePrefab();

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new EvictingPool<BoxCollider>(prefab, maxActive: 0));

            DestroyImmediate(prefab);
        }

        [Test]
        public void Constructor_NegativeEvictBuffer_ThrowsArgumentOutOfRange()
        {
            var prefab = CreatePrefab();

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new EvictingPool<BoxCollider>(prefab, maxActive: 2, evictBuffer: -1));

            DestroyImmediate(prefab);
        }

        [Test]
        public void Get_AtCapacity_EvictsOldestFIFO()
        {
            // Unity ObjectPool is a LIFO stack: the evicted 'a' is returned to the pool and
            // immediately reused as 'c'. We verify FIFO eviction via the invariants that
            // 'b' stays active (it was newer than 'a'), and ActiveCount stays at maxActive.
            var prefab = CreatePrefab();
            var pool = new EvictingPool<BoxCollider>(prefab, maxActive: 2);

            var a = pool.Get();
            var b = pool.Get();

            Assert.AreEqual(2, pool.ActiveCount);

            var c = pool.Get();

            Assert.AreEqual(2, pool.ActiveCount, "active count should stay at maxActive after eviction");
            Assert.IsTrue(b.gameObject.activeSelf, "non-oldest 'b' must not be evicted");
            Assert.IsTrue(c.gameObject.activeSelf);
            Assert.AreSame(a, c, "evicted 'a' is reused as 'c' via the LIFO pool");

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void Get_AtCapacityWithOnEvict_DefersReleaseToCallback()
        {
            var prefab = CreatePrefab();
            BoxCollider? evicted = null;
            Action<BoxCollider>? storedRelease = null;
            var pool = new EvictingPool<BoxCollider>(
                prefab,
                maxActive: 1,
                onEvict: (item, release) =>
                {
                    evicted = item;
                    storedRelease = release;
                });

            var a = pool.Get();
            var b = pool.Get(); // evicts a via callback

            Assert.AreSame(a, evicted, "onEvict receives the oldest item");
            Assert.IsTrue(a.gameObject.activeSelf, "a stays active until release is invoked (fade-out phase)");
            Assert.AreEqual(1, pool.ActiveCount);

            storedRelease!.Invoke(a); // simulate fade-out completion

            Assert.IsFalse(a.gameObject.activeSelf);

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void Release_AfterOnEvictCallbackReleased_DoesNotThrow()
        {
            // M8-adjacent invariant: a user Release(a) after onEvict has already released 'a'
            // must be a safe no-op — no throw, no double release.
            var prefab = CreatePrefab();
            Action<BoxCollider>? storedRelease = null;
            var pool = new EvictingPool<BoxCollider>(
                prefab,
                maxActive: 1,
                onEvict: (_, release) => storedRelease = release);

            var a = pool.Get();
            pool.Get(); // evicts a

            storedRelease!.Invoke(a); // user completes fade-out

            Assert.DoesNotThrow(() => pool.Release(a), "repeat Release on an already-released instance must be safe");

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void ReleaseAll_BypassesOnEvict_ReleasesEverything()
        {
            // M8 regression: ReleaseAll exists and does not invoke onEvict.
            var prefab = CreatePrefab();
            int evictCalls = 0;
            var pool = new EvictingPool<BoxCollider>(
                prefab,
                maxActive: 4,
                onEvict: (_, release) => { evictCalls++; release(_); });

            pool.Get();
            pool.Get();
            pool.Get();

            pool.ReleaseAll();

            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(0, evictCalls, "ReleaseAll must not invoke onEvict");

            pool.Dispose();
            DestroyImmediate(prefab);
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            var prefab = CreatePrefab();
            var pool = new EvictingPool<BoxCollider>(prefab, maxActive: 2);
            pool.Dispose();

            Assert.DoesNotThrow(() => pool.Dispose());

            DestroyImmediate(prefab);
        }

        [Test]
        public void Get_AfterDispose_ThrowsObjectDisposed()
        {
            var prefab = CreatePrefab();
            var pool = new EvictingPool<BoxCollider>(prefab, maxActive: 2);
            pool.Dispose();

            Assert.Throws<ObjectDisposedException>(() => pool.Get());

            DestroyImmediate(prefab);
        }
    }

    public class DescriptionAttributeTests
    {
        [Description("test-description-text")]
        private class DescribedComponent : MonoBehaviour { }

        private class UndescribedComponent : MonoBehaviour { }

        [Test]
        public void Attribute_ReadViaReflection_ReturnsText()
        {
            var attr = typeof(DescribedComponent).GetCustomAttribute<DescriptionAttribute>();

            Assert.IsNotNull(attr);
            Assert.AreEqual("test-description-text", attr!.Description);
        }

        [Test]
        public void Attribute_AbsentOnType_ReturnsNull()
        {
            var attr = typeof(UndescribedComponent).GetCustomAttribute<DescriptionAttribute>();

            Assert.IsNull(attr);
        }
    }
}
