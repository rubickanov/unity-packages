using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class AspectReplicatorEventCapTests
    {
        // Regression #18 — ISSUES.md. The cap (256) must be enforced on _eventBindings
        // after OnNetworkSpawn's scan phase. The reason is subtle and worth spelling out
        // so a future reader doesn't "simplify" the trim away: the event-index argument
        // to SubscribeAsAuthority is a byte, obtained from `(byte)i` in the subscribe loop.
        // At i == 256 the cast wraps to 0 and binding #256 would collide with binding #0 —
        // peers would silently route event 256's payload into event 0's Subject<T> and
        // vice-versa. There would be no crash, no log, no RPC error — just wrong data.
        // Symmetric with #2 (64-field cap for dirty mask).

        [Test]
        public void EnforceEventBindingCap_WithTwoHundredFiftySevenBindings_TrimsToExactlyTwoHundredFiftySix_RegressionEighteen()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;

            var bindings = BuildBindings(257);
            try
            {
                AspectReplicator.EnforceEventBindingCap(ref bindings, "TestEntity");
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(256, bindings.Length,
                "array must be trimmed exactly to 256 so byte-indexed event IDs never collide");
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("max is 256")),
                "trim must emit a LogError — silent truncation would hide a real data-routing bug");
        }

        [Test]
        public void EnforceEventBindingCap_WithExactlyTwoHundredFiftySixBindings_DoesNotTrimAndDoesNotLog()
        {
            // Edge-case invariant: 256 is valid (last index = 255, fits in a byte).
            // A future `> 256` → `>= 256` off-by-one would wrongly truncate one real
            // binding here; this test catches that.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;

            var bindings = BuildBindings(256);
            try
            {
                AspectReplicator.EnforceEventBindingCap(ref bindings, "TestEntity");
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(256, bindings.Length, "256 is the valid maximum — must not be trimmed");
            Assert.IsFalse(
                capture.Captured.Any(e => e.type == LogType.Error),
                "no LogError when the array is already at the cap");
        }

        // ---- Helpers ------------------------------------------------------------

        private static ReplicatedEventBinding[] BuildBindings(int count)
        {
            var result = new ReplicatedEventBinding[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ReplicatedEventBindingFactory.Create(
                    new Subject<int>(), typeof(int), AuthorityMode.Server, Reliability.Reliable);
            }
            return result;
        }

        private sealed class CapturingLogHandler : ILogHandler
        {
            private readonly List<(LogType type, string message)> _captured = new();
            public IReadOnlyList<(LogType type, string message)> Captured => _captured;

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
                => _captured.Add((logType, string.Format(format, args)));

            public void LogException(System.Exception exception, UnityEngine.Object context)
                => _captured.Add((LogType.Exception, exception.Message));
        }
    }
}
