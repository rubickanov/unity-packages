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

        private class TestSingleton : SingletonMonoEntity<TestSingleton> { }
    }
}
