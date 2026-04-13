using System.Reflection;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class EntityInjectorTests
    {
        [TearDown]
        public void TearDown()
        {
            // EntityInjector holds a static delegate — reset so tests don't leak into each other.
            EntityInjector.ClearInjector();
        }

        [Test]
        public void Invoke_AfterSetInjector_DelegateReceivesGameObject()
        {
            GameObject received = null;
            EntityInjector.SetInjector(go => received = go);
            var expected = new GameObject(nameof(EntityInjectorTests));

            try
            {
                EntityInjector.Invoke(expected);

                Assert.AreSame(expected, received);
            }
            finally
            {
                Object.DestroyImmediate(expected);
            }
        }

        [Test]
        public void Invoke_WithoutSetInjector_IsNoOp()
        {
            var go = new GameObject(nameof(EntityInjectorTests));

            try
            {
                Assert.DoesNotThrow(() => EntityInjector.Invoke(go));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClearInjector_AfterSetInjector_StopsDelegateInvocation()
        {
            int callCount = 0;
            EntityInjector.SetInjector(_ => callCount++);
            EntityInjector.ClearInjector();
            var go = new GameObject(nameof(EntityInjectorTests));

            try
            {
                EntityInjector.Invoke(go);

                Assert.AreEqual(0, callCount);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResetOnPlayStart_AfterSetInjector_ClearsDelegate()
        {
            // [RuntimeInitializeOnLoadMethod] runs automatically on play-mode enter; edit-mode
            // tests reach the method via reflection to prove it nulls the static delegate. This
            // is the Domain-Reload-off safety net: a delegate captured last session must not
            // survive into this one.
            int callCount = 0;
            EntityInjector.SetInjector(_ => callCount++);

            typeof(EntityInjector)
                .GetMethod("ResetOnPlayStart", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);

            var go = new GameObject(nameof(EntityInjectorTests));
            try
            {
                EntityInjector.Invoke(go);

                Assert.AreEqual(0, callCount);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetInjector_SameDelegateTwice_DoesNotWarn()
        {
            System.Action<GameObject> same = _ => { };
            EntityInjector.SetInjector(same);

            // Second set with the same reference must be a silent no-op — hot-reload
            // workflows with domain reload disabled re-enter SetInjector repeatedly.
            LogAssert.NoUnexpectedReceived();
            Assert.DoesNotThrow(() => EntityInjector.SetInjector(same));
        }
    }
}
