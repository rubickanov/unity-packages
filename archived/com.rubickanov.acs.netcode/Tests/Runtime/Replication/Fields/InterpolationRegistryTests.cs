using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class InterpolationRegistryTests
    {
        // Registry keeps a static Dictionary<ReactiveProperty<T>, IInterpolatedBinding<T>>
        // per closed generic. Clear it between tests so an entry from one test cannot
        // trip the double-register assert in the next.

        [SetUp]
        public void ClearRegistry()
        {
            ClearBindingsDictionary(typeof(float));
            ClearBindingsDictionary(typeof(int));
        }

        private static void ClearBindingsDictionary(System.Type t)
        {
            var closed = typeof(InterpolationRegistry<>).MakeGenericType(t);
            var field = closed.GetField("Bindings",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "InterpolationRegistry<T> must have a private static 'Bindings' dictionary — rename detected?");
            var dict = (System.Collections.IDictionary)field.GetValue(null);
            dict.Clear();
        }

        [Test]
        public void Register_SameReactivePropertyTwice_LogsError()
        {
            // Double-register silently overwrites the prior binding, which masks ownership-transfer
            // cleanup bugs (ex-owner's binding stays registered, new owner's overwrites it). The
            // previous Debug.Assert stripped from Release builds — Batch 5 replaced it with an
            // unconditional Debug.LogError so the signal survives in shipping builds too.
            var reactive = new ReactiveProperty<float>(0f);
            var binding = new StubBinding<float>();

            InterpolationRegistry<float>.Register(reactive, binding);

            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                InterpolationRegistry<float>.Register(reactive, binding);
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("double-register")),
                "Registering a binding for the same ReactiveProperty twice must log an unconditional error");
        }

        [Test]
        public void TryGetInterpolatedValue_RegisteredBinding_ReturnsItsInterpolatedValue()
        {
            var reactive = new ReactiveProperty<float>(0f);
            var binding = new StubBinding<float> { Value = 42f };
            InterpolationRegistry<float>.Register(reactive, binding);

            bool found = InterpolationRegistry<float>.TryGetInterpolatedValue(reactive, out var value);

            Assert.IsTrue(found);
            Assert.AreEqual(42f, value, 1e-6f);
        }

        [Test]
        public void TryGetInterpolatedValue_UnregisteredProperty_ReturnsFalse()
        {
            var reactive = new ReactiveProperty<float>(7f);

            bool found = InterpolationRegistry<float>.TryGetInterpolatedValue(reactive, out var value);

            Assert.IsFalse(found);
            Assert.AreEqual(0f, value, 1e-6f);
        }

        [Test]
        public void Unregister_RemovesEntry_SoRegisterAgainDoesNotLogError()
        {
            // After OnDespawn the registry entry must be gone, so a fresh spawn with the
            // same ReactiveProperty (e.g. pooled entity reuse) can register cleanly.
            var reactive = new ReactiveProperty<float>(0f);
            var binding = new StubBinding<float>();
            InterpolationRegistry<float>.Register(reactive, binding);
            InterpolationRegistry<float>.Unregister(reactive);

            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                InterpolationRegistry<float>.Register(reactive, binding);
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.IsFalse(
                capture.Captured.Any(e => e.type == LogType.Error),
                "Unregister followed by Register on the same ReactiveProperty must not log an error");
        }

        private sealed class StubBinding<T> : IInterpolatedBinding<T> where T : unmanaged
        {
            public T Value;
            public T InterpolatedValue => Value;
        }

        private sealed class CapturingLogHandler : ILogHandler
        {
            private readonly List<(LogType type, string message)> _captured = new();
            public IReadOnlyList<(LogType type, string message)> Captured => _captured;

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                _captured.Add((logType, string.Format(format, args)));
            }

            public void LogException(System.Exception exception, UnityEngine.Object context)
            {
                _captured.Add((LogType.Exception, exception.Message));
            }
        }
    }
}
