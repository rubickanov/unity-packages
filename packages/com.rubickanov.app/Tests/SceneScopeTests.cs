using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Rubickanov.App.Tests
{
    public sealed class SceneScopeTests
    {
        private static readonly List<string> Calls = new();
        private static bool _throws;
        private GameObject _made;

        [SetUp]
        public void SetUp()
        {
            Calls.Clear();
            _throws = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_made != null)
            {
                UnityEngine.Object.DestroyImmediate(_made);
            }
        }

        [Test]
        public void Create_ServicesMade_HeldUntilDisposedThenGone()
        {
            TestScope scope = Make();
            Assert.That(scope.Services, Is.Not.Null);
            Assert.That(scope.gameObject.name, Is.EqualTo(nameof(TestScope)));

            scope.Dispose();

            Assert.That(scope.Services, Is.Null);
            Assert.That(Calls, Is.EqualTo(new[] { "owned gone" }));
        }

        [Test]
        public void Create_ServicesThrow_WhatWasMadeGoesAndTheGameIsTold()
        {
            _throws = true;

            LogAssert.Expect(LogType.Exception, new Regex("half made"));
            TestScope scope = Make();

            Assert.That(scope.Services, Is.Null);
            Assert.That(Calls, Is.EqualTo(new[] { "owned gone", "failed" }));
        }

        [Test]
        public void Destroy_Scope_LetsTheServicesGo()
        {
            TestScope scope = Make();

            UnityEngine.Object.DestroyImmediate(scope.gameObject);

            Assert.That(Calls, Is.EqualTo(new[] { "owned gone" }));
        }

        private TestScope Make()
        {
            TestScope scope = SceneScope<object, List<string>>.Create<TestScope>(SceneManager.GetActiveScene(), new object());
            _made = scope.gameObject;
            return scope;
        }

        private sealed class Owned : IDisposable
        {
            public void Dispose() => Calls.Add("owned gone");
        }

        // Edit mode calls OnDestroy only on a script that runs in edit mode.
        [ExecuteAlways]
        private sealed class TestScope : SceneScope<object, List<string>>
        {
            protected override List<string> CreateServices(object app, Scene scene, Lifecycle life)
            {
                life.Own(new Owned());
                if (_throws)
                {
                    throw new InvalidOperationException("half made");
                }

                return Calls;
            }

            protected override void OnFailed(object app, Scene scene) => Calls.Add("failed");
        }
    }
}
