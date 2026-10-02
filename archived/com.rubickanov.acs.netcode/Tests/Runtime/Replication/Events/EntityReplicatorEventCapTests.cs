using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class EntityReplicatorEventCapTests
    {
        // The cap (256) must be enforced on _eventBindings
        // after OnNetworkSpawn's scan phase. The reason is subtle and worth spelling out
        // so a future reader doesn't "simplify" the check away: the event-index argument
        // to SubscribeAsAuthority is a byte, obtained from `(byte)i` in the subscribe loop.
        // At i == 256 the cast wraps to 0 and binding #256 would collide with binding #0 —
        // peers would silently route event 256's payload into event 0's Subject<T> and
        // vice-versa. There would be no crash, no log, no RPC error — just wrong data.
        //
        // The contract is NOT "trim to 256": silent truncation is strictly worse than an
        // abort, because two peers dropping different excess bindings would desync their
        // bitmask positions. OnNetworkSpawn aborts registration on overflow — the helper
        // here is the predicate that signals "over cap, abort".

        [Test]
        public void ExceedsEventBindingCap_WithTwoHundredFiftySevenBindings_ReportsOverflowAndLogsError_RegressionEighteen()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;

            bool overflowed;
            try
            {
                overflowed = EntityReplicator.ExceedsEventBindingCap(257, "TestEntity");
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.IsTrue(overflowed,
                "257 > 256 must report overflow so OnNetworkSpawn aborts before registering");
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("max is 256")),
                "overflow must emit a LogError — silent truncation would hide a real data-routing bug");
        }

        [Test]
        public void ExceedsEventBindingCap_WithExactlyTwoHundredFiftySixBindings_DoesNotReportOverflow()
        {
            // Edge-case invariant: 256 is valid (last index = 255, fits in a byte).
            // A future `> 256` → `>= 256` off-by-one would wrongly reject one real
            // binding here; this test catches that.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;

            bool overflowed;
            try
            {
                overflowed = EntityReplicator.ExceedsEventBindingCap(256, "TestEntity");
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.IsFalse(overflowed, "256 is the valid maximum — must not report overflow");
            Assert.IsFalse(
                capture.Captured.Any(e => e.type == LogType.Error),
                "no LogError when the array is at the cap exactly");
        }

        [Test]
        public void ExceedsFieldBindingCap_WithTwoHundredFiftySevenBindings_ReportsOverflowAndLogsError_RegressionThree()
        {
            // Mirrors the event-cap predicate: the field bitmask is also position-indexed,
            // so two peers clamping different excess bindings would drift mask bits and
            // write state payloads into the wrong reactive properties.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;

            bool overflowed;
            try
            {
                overflowed = EntityReplicator.ExceedsFieldBindingCap(257, "TestEntity");
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.IsTrue(overflowed,
                "257 > 256 must report overflow so OnNetworkSpawn aborts before registering");
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("max is 256")),
                "overflow must emit a LogError — silent truncation would hide a real data-routing bug");
        }

        [Test]
        public void ExceedsFieldBindingCap_WithExactlyTwoHundredFiftySixBindings_DoesNotReportOverflow()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;

            bool overflowed;
            try
            {
                overflowed = EntityReplicator.ExceedsFieldBindingCap(256, "TestEntity");
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.IsFalse(overflowed, "256 is the valid maximum — must not report overflow");
            Assert.IsFalse(
                capture.Captured.Any(e => e.type == LogType.Error),
                "no LogError when the count is at the cap exactly");
        }

        // ---- Helpers ------------------------------------------------------------

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
