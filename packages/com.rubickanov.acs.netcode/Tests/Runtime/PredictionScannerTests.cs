using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class PredictionScannerTests
    {
        // See ReplicationScannerTests for the rationale: the scanner's static per-type
        // cache outlives a single test run, so negative tests (which expect a LogError
        // on the FIRST scan) would silently stop seeing the error on subsequent runs
        // without a cache reset. Clear both the Cache and UnmanagedCache before each
        // test.

        [SetUp]
        public void ClearScannerStaticCaches()
        {
            ClearStaticDictionary("Cache");
            ClearStaticDictionary("UnmanagedCache");
        }

        private static void ClearStaticDictionary(string fieldName)
        {
            var field = typeof(PredictionScanner).GetField(fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field,
                $"PredictionScanner must have a private static field '{fieldName}' — rename detected?");
            var dict = (IDictionary)field.GetValue(null);
            dict.Clear();
        }

        // ---- Field ordering -----------------------------------------------------

        [Test]
        public void Scan_FieldsDeclaredInReverseAlphabeticalOrder_ReturnedAlphabetically()
        {
            var fields = PredictionScanner.Scan(new OrderingAspect());

            Assert.AreEqual(2, fields.Length);
            Assert.AreEqual("Alpha", fields[0].Field.Name);
            Assert.AreEqual("Zebra", fields[1].Field.Name);
        }

        [Test]
        public void Scan_ResultsAreCachedPerTypeAndReturnSameArrayReference()
        {
            var first = PredictionScanner.Scan(new CachingAspect());
            var second = PredictionScanner.Scan(new CachingAspect());
            Assert.AreSame(first, second);
        }

        // ---- Inheritance --------------------------------------------------------

        [Test]
        public void Scan_DerivedAspect_IncludesBasePredictedFields()
        {
            var fields = PredictionScanner.Scan(new DerivedAspect());

            Assert.AreEqual(2, fields.Length);
            Assert.AreEqual("BaseValue", fields[0].Field.Name);
            Assert.AreEqual("DerivedValue", fields[1].Field.Name);
        }

        // ---- Value type carried on FieldInfo ------------------------------------

        [Test]
        public void Scan_ValueTypeMatchesReactivePropertyGeneric()
        {
            var fields = PredictionScanner.Scan(new ValueTypeAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(typeof(Vector3), fields[0].ValueType);
        }

        // ---- Negative: unsupported value types ----------------------------------

        [Test]
        public void Scan_ReactivePropertyOfString_LogsErrorAndOmitsField()
        {
            // Captures Debug.LogError via logHandler swap so the expected error
            // does not surface in the Unity Console. Same technique as
            // ReplicationScannerTests — see that file for the rationale.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            PredictedFieldInfo[] fields;
            try
            {
                fields = PredictionScanner.Scan(new InvalidStringPredictedAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("not unmanaged")),
                "Scanner must emit a LogError when [Predicted] targets a ReactiveProperty of a managed type");
        }

        [Test]
        public void Scan_PredictedOnNonReactiveProperty_LogsErrorAndOmitsField()
        {
            // A user attaching [Predicted] to a plain int is a programming error we
            // want surfaced — the scanner must not silently ignore it.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            PredictedFieldInfo[] fields;
            try
            {
                fields = PredictionScanner.Scan(new InvalidPlainFieldAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("ReactiveProperty")),
                "Scanner must emit a LogError when [Predicted] is attached to a non-ReactiveProperty field");
        }

        // ---- Negative: [Predicted] on owner-auth field --------------------------

        [Test]
        public void Scan_PredictedOnOwnerAuthField_LogsWarningAndOmitsField()
        {
            // Owner is already the source of truth — reconcile would run replay
            // on self-relayed batches and accelerate the owner by one Simulate
            // pass per tick. The scanner drops the field with a warning so the
            // prediction pipeline stays silent for owner-auth state.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            PredictedFieldInfo[] fields;
            try
            {
                fields = PredictionScanner.Scan(new OwnerAuthPredictedAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Warning && e.message.Contains("Authority is Owner")),
                "Scanner must emit a LogWarning when [Predicted] targets a [ReplicatedState(Authority = Owner)] field");
        }

        [Test]
        public void Scan_PredictedOnServerAuthField_IsKept()
        {
            // Sanity: the warning path only fires for owner-auth. Explicit
            // Authority = Server (and the default, which is Server) still scan.
            var fields = PredictionScanner.Scan(new ServerAuthPredictedAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Position", fields[0].Field.Name);
        }

        // ---- HasPredictedFields shortcut ----------------------------------------

        [Test]
        public void HasPredictedFields_AspectWithMarkedField_ReturnsTrue()
        {
            Assert.IsTrue(PredictionScanner.HasPredictedFields(new CachingAspect()));
        }

        [Test]
        public void HasPredictedFields_AspectWithoutMarkedField_ReturnsFalse()
        {
            Assert.IsFalse(PredictionScanner.HasPredictedFields(new NoMarkerAspect()));
        }

        // ---- Test aspects -------------------------------------------------------

        private class OrderingAspect
        {
            [Predicted] public ReactiveProperty<int> Zebra = new ReactiveProperty<int>(0);
            [Predicted] public ReactiveProperty<int> Alpha = new ReactiveProperty<int>(0);
        }

        private class CachingAspect
        {
            [Predicted] public ReactiveProperty<int> Value = new ReactiveProperty<int>(0);
        }

        private class BaseAspect
        {
            [Predicted] public ReactiveProperty<int> BaseValue = new ReactiveProperty<int>(0);
        }

        private class DerivedAspect : BaseAspect
        {
            [Predicted] public ReactiveProperty<float> DerivedValue = new ReactiveProperty<float>(0f);
        }

        private class ValueTypeAspect
        {
            [Predicted] public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class InvalidStringPredictedAspect
        {
            [Predicted] public ReactiveProperty<string> Bad = new ReactiveProperty<string>(string.Empty);
        }

        private class InvalidPlainFieldAspect
        {
#pragma warning disable CS0649
            [Predicted] public int Bad;
#pragma warning restore CS0649
        }

        private class NoMarkerAspect
        {
            public ReactiveProperty<int> Value = new ReactiveProperty<int>(0);
        }

        private class OwnerAuthPredictedAspect
        {
            [ReplicatedState(Authority = AuthorityMode.Owner)]
            [Predicted]
            public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class ServerAuthPredictedAspect
        {
            [ReplicatedState(Authority = AuthorityMode.Server)]
            [Predicted]
            public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        // ---- Log capture --------------------------------------------------------
        private sealed class CapturingLogHandler : ILogHandler
        {
            private readonly List<(LogType type, string message)> _captured = new();
            public IReadOnlyList<(LogType type, string message)> Captured => _captured;

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                _captured.Add((logType, string.Format(format, args)));
            }

            public void LogException(System.Exception exception, Object context)
            {
                _captured.Add((LogType.Exception, exception.Message));
            }
        }
    }
}
