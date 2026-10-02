using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Edit-mode tests for <see cref="SingletonMonoEntity{T}"/> duplicate handling.
    /// Unity does not auto-fire MonoBehaviour lifecycle on <c>AddComponent</c> /
    /// <c>DestroyImmediate</c> in edit mode, so Awake and OnDestroy are invoked via
    /// reflection — matches the pattern used in <see cref="MonoEntityTests"/>.
    /// </summary>
    [TestFixture]
    public class SingletonMonoEntityTests
    {
        // Production Awake calls Destroy(gameObject) on a duplicate; that's correct at runtime
        // but in edit mode Unity logs an error ("Destroy may not be called from edit mode!"),
        // which NUnit promotes to a test failure unless we whitelist it.
        private static readonly Regex EditModeDestroyError =
            new Regex("Destroy may not be called from edit mode");

        [TearDown]
        public void TearDown()
        {
            // Reset the static singleton slot so tests don't leak into each other.
            typeof(SingletonMonoEntity<TestSingleton>)
                .GetProperty(nameof(SingletonMonoEntity<TestSingleton>.Instance),
                    BindingFlags.Public | BindingFlags.Static)!
                .SetValue(null, null);
        }

        [Test]
        public void Awake_Duplicate_OnDestroy_DoesNotFireDestroyed()
        {
            var firstGo = new GameObject("first");
            var first = firstGo.AddComponent<TestSingleton>();
            InvokeAwake(first);

            var dupGo = new GameObject("dup");
            var dup = dupGo.AddComponent<TestSingleton>();
            int destroyedCount = 0;
            dup.Destroyed += _ => destroyedCount++;
            LogAssert.Expect(LogType.Error, EditModeDestroyError);
            InvokeAwake(dup);
            InvokeOnDestroy(dup);

            Assert.AreEqual(0, destroyedCount,
                "Duplicate was never observed as the singleton and never registered aspects — " +
                "firing Destroyed for it confuses subscribers that picked it up in the one frame " +
                "between Awake and the deferred OnDestroy.");

            Object.DestroyImmediate(dupGo);
            Object.DestroyImmediate(firstGo);
        }

        [Test]
        public void Awake_Duplicate_OnDestroy_DoesNotClearInstance()
        {
            var firstGo = new GameObject("first");
            var first = firstGo.AddComponent<TestSingleton>();
            InvokeAwake(first);

            var dupGo = new GameObject("dup");
            var dup = dupGo.AddComponent<TestSingleton>();
            LogAssert.Expect(LogType.Error, EditModeDestroyError);
            InvokeAwake(dup);
            InvokeOnDestroy(dup);

            Assert.AreSame(first, TestSingleton.Instance,
                "Original Instance must survive the duplicate's OnDestroy.");

            Object.DestroyImmediate(dupGo);
            Object.DestroyImmediate(firstGo);
        }

        [Test]
        public void ResetInstanceOnPlayStart_WithLiveInstance_NullsInstance()
        {
            // [RuntimeInitializeOnLoadMethod] runs automatically on play-mode enter; edit-mode
            // tests reach the method via reflection to prove it nulls Instance. This is the
            // Domain-Reload-off safety net: a stale reference from the previous session (e.g.
            // OnDestroy skipped on cold exit) must not survive into the next one.
            var go = new GameObject("original");
            var singleton = go.AddComponent<TestSingleton>();
            InvokeAwake(singleton);
            Assert.AreSame(singleton, TestSingleton.Instance, "Precondition: Awake set Instance.");

            typeof(SingletonMonoEntity<TestSingleton>)
                .GetMethod("ResetInstanceOnPlayStart", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);

            Assert.IsNull(TestSingleton.Instance);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void OnDestroy_DuplicateWithRegisteredAspect_UnregistersFromWorld()
        {
            // Production scenario: two SingletonMonoEntity GameObjects enter the scene;
            // the second is a duplicate. Destroy(gameObject) is deferred to end-of-frame,
            // so a sibling EntityComponent on the duplicate's GameObject can still run
            // Awake and call Context.Require<T>() — registering the duplicate's aspect in
            // World._registry's per-aspect index. If OnDestroy early-returns without
            // unregistering, the duplicate stays in the registry forever and Query<T>
            // iterates a dead reference.
            var worldGo = new GameObject(nameof(MonoWorld));
            var firstGo = new GameObject("first");
            var dupGo = new GameObject("dup");
            try
            {
                var world = worldGo.AddComponent<MonoWorld>();
                InvokeMonoWorldAwake(world);

                var first = firstGo.AddComponent<TestSingleton>();
                InvokeAwake(first);

                var dup = dupGo.AddComponent<TestSingleton>();
                LogAssert.Expect(LogType.Error, EditModeDestroyError);
                InvokeAwake(dup);
                // Mimic the sibling-EntityComponent's Awake: request an aspect on the duplicate
                // while it's still alive. This mirrors what Context.Require<T>() would do on a
                // child GameObject whose Awake runs in the frame before Destroy lands.
                dup.Require<TestAspect>();

                CollectionAssert.Contains(
                    MonoWorld.Instance!.World.Registry.GetAllWith(typeof(TestAspect)),
                    dup,
                    "Precondition: the duplicate registered its aspect in World before OnDestroy.");

                InvokeOnDestroy(dup);

                CollectionAssert.DoesNotContain(
                    MonoWorld.Instance!.World.Registry.GetAllWith(typeof(TestAspect)),
                    dup,
                    "Duplicate must unregister its aspects in OnDestroy — otherwise the registry " +
                    "holds a reference to a destroyed entity for the rest of the session.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dupGo);
                UnityEngine.Object.DestroyImmediate(firstGo);
                UnityEngine.Object.DestroyImmediate(worldGo);
                typeof(SingletonMonoEntity<MonoWorld>)
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                    .SetValue(null, null);
                typeof(World)
                    .GetMethod("ForceResetCurrent", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null);
            }
        }

        [Test]
        public void OnDestroy_OriginalInstance_StillFiresDestroyed()
        {
            var go = new GameObject("original");
            var singleton = go.AddComponent<TestSingleton>();
            InvokeAwake(singleton);
            int destroyedCount = 0;
            singleton.Destroyed += _ => destroyedCount++;

            InvokeOnDestroy(singleton);

            Assert.AreEqual(1, destroyedCount,
                "Non-duplicate destruction must fire Destroyed exactly once (regression guard).");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ResetAllOnPlayStart_AfterAwake_NullsInstance()
        {
            // End-to-end path the play-start hook actually takes: Awake self-registers the
            // type's reset with the non-generic dispatcher, and the dispatcher nulls Instance.
            // Replaces the old design where the dispatcher discovered types by walking every
            // assembly's GetTypes() before the first frame.
            var go = new GameObject("original");
            var singleton = go.AddComponent<TestSingleton>();
            InvokeAwake(singleton);
            Assert.AreSame(singleton, TestSingleton.Instance, "Precondition: Awake set Instance.");

            SingletonMonoEntityResetter.ResetAllOnPlayStart();

            Assert.IsNull(TestSingleton.Instance);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Awake_SecondInstanceOfSameType_DoesNotRegisterASecondReset()
        {
            // The registration guard is per closed generic type. Asserted as a delta because
            // the dispatcher's list is process-wide and other fixtures contribute to it.
            var firstGo = new GameObject("first");
            var first = firstGo.AddComponent<TestSingleton>();
            InvokeAwake(first);

            int before = SingletonMonoEntityResetter.RegisteredCountForTests;

            var secondGo = new GameObject("second");
            var second = secondGo.AddComponent<TestSingleton>();
            LogAssert.Expect(LogType.Error, EditModeDestroyError);
            InvokeAwake(second);

            Assert.AreEqual(before, SingletonMonoEntityResetter.RegisteredCountForTests);

            Object.DestroyImmediate(secondGo);
            Object.DestroyImmediate(firstGo);
        }

        private static void InvokeAwake(TestSingleton entity)
        {
            typeof(SingletonMonoEntity<TestSingleton>)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(entity, null);
        }

        private static void InvokeOnDestroy(TestSingleton entity)
        {
            typeof(SingletonMonoEntity<TestSingleton>)
                .GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(entity, null);
        }

        private static void InvokeMonoWorldAwake(MonoWorld world)
        {
            typeof(MonoWorld)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                .Invoke(world, null);
        }

        private class TestSingleton : SingletonMonoEntity<TestSingleton> { }

        private class TestAspect : IEntityAspect { }
    }
}
