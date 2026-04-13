using System.Collections;
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
    public class ReplicationScannerTests
    {
        // NOTE ON CACHE HYGIENE:
        // ReplicationScanner keeps three static per-Type dictionaries (StateCache,
        // EventCache, UnmanagedCache) that live for the whole Unity domain — not just
        // a single test run. Unique nested types per test protect against pollution
        // WITHIN a run, but a second Run in Test Runner without a domain reload still
        // sees the previous run's cache, and negative tests that expect a LogError on
        // the first scan stop seeing that error (cache hit → early return before the
        // Debug.LogError line). [SetUp] below clears all three caches via reflection so
        // every test starts against a cold scanner.

        [SetUp]
        public void ClearScannerStaticCaches()
        {
            ClearStaticDictionary("StateCache");
            ClearStaticDictionary("EventCache");
            ClearStaticDictionary("UnmanagedCache");
        }

        private static void ClearStaticDictionary(string fieldName)
        {
            var field = typeof(ReplicationScanner).GetField(fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            // Meaningful precondition: if production renames one of these fields the
            // test suite must fail loudly rather than silently leak cached results.
            Assert.IsNotNull(field,
                $"ReplicationScanner must have a private static field '{fieldName}' — rename detected?");
            var dict = (IDictionary)field.GetValue(null);
            dict.Clear();
        }

        // ---- Field ordering -----------------------------------------------------

        [Test]
        public void Scan_FieldsDeclaredInReverseAlphabeticalOrder_ReturnedAlphabetically()
        {
            var fields = ReplicationScanner.Scan(new OrderingAspect());

            Assert.AreEqual(2, fields.Length);
            Assert.AreEqual("Alpha", fields[0].Field.Name);
            Assert.AreEqual("Zebra", fields[1].Field.Name);
        }

        [Test]
        public void Scan_ResultsAreCachedPerTypeAndReturnSameArrayReference()
        {
            // AreSame, not AreEqual — the cache must return the identical array instance
            // to avoid re-running reflection on every network spawn.
            var first = ReplicationScanner.Scan(new CachingAspect());
            var second = ReplicationScanner.Scan(new CachingAspect());
            Assert.AreSame(first, second);
        }

        // ---- Inheritance --------------------------------------------------------

        [Test]
        public void Scan_DerivedAspect_IncludesBaseClassReplicatedFields()
        {
            var fields = ReplicationScanner.Scan(new DerivedAspect());

            // Derived declares 'DerivedValue', Base declares 'BaseValue'. Expected
            // alphabetical order: BaseValue, DerivedValue.
            Assert.AreEqual(2, fields.Length);
            Assert.AreEqual("BaseValue", fields[0].Field.Name);
            Assert.AreEqual("DerivedValue", fields[1].Field.Name);
        }

        // ---- AuthorityMode & InterpolationMode ---------------------------------

        [Test]
        public void Scan_AuthorityAndInterpolationAttributes_CarriedOnFieldInfo()
        {
            var fields = ReplicationScanner.Scan(new AttributedAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(AuthorityMode.Owner, fields[0].Authority);
            Assert.AreEqual(InterpolationMode.Linear, fields[0].Interpolation);
        }

        // ---- Predicted flag propagation ----------------------------------------

        [Test]
        public void Scan_PredictedFlag_CarriedOnFieldInfo()
        {
            // Default Predicted=false; explicit Predicted=true on a server-auth field
            // survives the scan. Mirrors the previous [Predicted] marker behavior now
            // folded into the unified [Replicated] attribute.
            var fields = ReplicationScanner.Scan(new PredictedFlagAspect());

            // Alphabetical: NonPredicted, Predicted.
            Assert.AreEqual(2, fields.Length);
            Assert.AreEqual("NonPredicted", fields[0].Field.Name);
            Assert.IsFalse(fields[0].Predicted);
            Assert.AreEqual("Predicted", fields[1].Field.Name);
            Assert.IsTrue(fields[1].Predicted);
        }

        [Test]
        public void Scan_PredictedOnOwnerAuthField_LogsWarningAndStripsPredictedFlag()
        {
            // Owner is already the source of truth — reconcile would run replay on
            // self-relayed batches and accelerate the owner by one Simulate pass per
            // tick. The scanner clears Predicted on the field (it still replicates)
            // with a warning so downstream PredictionScanner never sees it.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new OwnerAuthPredictedAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Position", fields[0].Field.Name);
            Assert.IsFalse(fields[0].Predicted,
                "Predicted flag must be cleared on an owner-authoritative field");
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Warning && e.message.Contains("Authority is Owner")),
                "Scanner must emit a LogWarning when Predicted = true is combined with Authority = Owner");
        }

        // ---- Negative: unsupported value types ---------------------------------

        [Test]
        public void Scan_ReactivePropertyOfString_LogsErrorAndOmitsField()
        {
            // NOTE: We intercept Debug.unityLogger.logHandler instead of using LogAssert.Expect
            // because LogAssert.Expect does not suppress the message from Unity's Console —
            // the red error lines accumulate after every test run even when tests are green.
            // The CapturingLogHandler swallows the log silently while still letting us verify
            // that the real invariant (LogError was emitted with the expected text) holds.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new InvalidStringStateAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("not unmanaged")),
                "Scanner must emit a LogError explaining that the ReactiveProperty<string> type is not unmanaged");
        }

        [Test]
        public void Scan_ReplicatedOnNonReactiveProperty_LogsErrorAndOmitsField()
        {
            // [Replicated] on a plain int is a programming error we want surfaced —
            // previously caught by PredictionScanner for [Predicted] markers, now
            // caught here since the unified attribute is the only validation site.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new InvalidPlainFieldAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("ReactiveProperty")),
                "Scanner must emit a LogError when [Replicated] is attached to a non-ReactiveProperty field");
        }

        [Test]
        public void ScanEvents_SubjectOfManagedGeneric_LogsErrorAndOmitsEvent()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedEventInfo[] events;
            try
            {
                events = ReplicationScanner.ScanEvents(new InvalidListEventAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, events.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("not unmanaged")),
                "Scanner must emit a LogError explaining that the Subject<List<int>> type is not unmanaged");
        }

        // ---- Events: basic scan + ordering -------------------------------------

        [Test]
        public void ScanEvents_DeclaredInReverseAlphabeticalOrder_ReturnedAlphabetically()
        {
            var events = ReplicationScanner.ScanEvents(new EventOrderingAspect());

            Assert.AreEqual(2, events.Length);
            Assert.AreEqual("AlphaEvent", events[0].Field.Name);
            Assert.AreEqual("ZebraEvent", events[1].Field.Name);
        }

        // ---- Regression #5: bitmask ordering ------------------------------------

        [Test]
        public void Scan_FieldOrderStable_AcrossRepeatedInvocations_RegressionFive()
        {
            // Issue #5 is about bitmask indices staying stable between server and client
            // regardless of reflection traversal order. The scanner's sorted-by-name
            // guarantee is what makes the bitmask deterministic — this test locks that
            // contract in place so any future refactor that drops the sort is caught.
            var first = ReplicationScanner.Scan(new StabilityAspect());
            var namesFirst = new List<string>();
            for (int i = 0; i < first.Length; i++) namesFirst.Add(first[i].Field.Name);

            // Second call returns the cached array, but the ordering contract is what
            // we're asserting — do it explicitly.
            var second = ReplicationScanner.Scan(new StabilityAspect());
            var namesSecond = new List<string>();
            for (int i = 0; i < second.Length; i++) namesSecond.Add(second[i].Field.Name);

            CollectionAssert.AreEqual(namesFirst, namesSecond);
            CollectionAssert.AreEqual(new[] { "Apple", "Banana", "Cherry" }, namesFirst);
        }

        // ---- Test aspects -------------------------------------------------------
        // One per test to avoid static-cache contamination.

        private class OrderingAspect
        {
            [Replicated] public ReactiveProperty<int> Zebra = new ReactiveProperty<int>(0);
            [Replicated] public ReactiveProperty<int> Alpha = new ReactiveProperty<int>(0);
        }

        private class CachingAspect
        {
            [Replicated] public ReactiveProperty<int> Value = new ReactiveProperty<int>(0);
        }

        private class BaseAspect
        {
            [Replicated] public ReactiveProperty<int> BaseValue = new ReactiveProperty<int>(0);
        }

        private class DerivedAspect : BaseAspect
        {
            [Replicated] public ReactiveProperty<float> DerivedValue = new ReactiveProperty<float>(0f);
        }

        private class AttributedAspect
        {
            [Replicated(Authority = AuthorityMode.Owner, Interpolation = InterpolationMode.Linear)]
            public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class PredictedFlagAspect
        {
            [Replicated(Predicted = true)]
            public ReactiveProperty<Vector3> Predicted = new ReactiveProperty<Vector3>(Vector3.zero);

            [Replicated]
            public ReactiveProperty<Vector3> NonPredicted = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class OwnerAuthPredictedAspect
        {
            [Replicated(Authority = AuthorityMode.Owner, Predicted = true)]
            public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class InvalidStringStateAspect
        {
            // string is a managed reference type — must be rejected.
            [Replicated] public ReactiveProperty<string> Bad = new ReactiveProperty<string>(string.Empty);
        }

        private class InvalidPlainFieldAspect
        {
#pragma warning disable CS0649
            [Replicated] public int Bad;
#pragma warning restore CS0649
        }

        private class InvalidListEventAspect
        {
            // List<int> is a managed reference type — must be rejected.
            [ReplicatedEvent] public Subject<List<int>> Bad = new Subject<List<int>>();
        }

        private class EventOrderingAspect
        {
            [ReplicatedEvent] public Subject<int> ZebraEvent = new Subject<int>();
            [ReplicatedEvent] public Subject<int> AlphaEvent = new Subject<int>();
        }

        private class StabilityAspect
        {
            [Replicated] public ReactiveProperty<int> Cherry = new ReactiveProperty<int>(0);
            [Replicated] public ReactiveProperty<int> Apple = new ReactiveProperty<int>(0);
            [Replicated] public ReactiveProperty<int> Banana = new ReactiveProperty<int>(0);
        }

        // ---- Log capture --------------------------------------------------------
        // Swaps in as Debug.unityLogger.logHandler while a negative test runs, so
        // expected Debug.LogError calls are captured for assertion but never reach
        // Unity's native logger — keeps the Console clean after the run.
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
