using System.Collections;
using System.Reflection;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class PredictionScannerTests
    {
        // PredictionScanner is now a thin filter over ReplicationScanner — validation
        // (ReactiveProperty shape, unmanaged value type, Owner + Predicted invariant)
        // lives on ReplicationScanner and is exercised by ReplicationScannerTests.
        // These tests lock in the filter's invariants: ordering, caching, inheritance
        // and the Predicted-flag selection.

        [SetUp]
        public void ClearScannerStaticCaches()
        {
            // PredictionScanner delegates to ReplicationScanner on cold reads, so
            // both caches must be cleared — otherwise a warmer ReplicationScanner
            // cache would serve stale outputs into a freshly cleared Prediction one.
            ClearStaticDictionary(typeof(PredictionScanner), "Cache");
            ClearStaticDictionary(typeof(ReplicationScanner), "StateCache");
            ClearStaticDictionary(typeof(ReplicationScanner), "EventCache");
            ClearStaticDictionary(typeof(ReplicationScanner), "UnmanagedCache");
        }

        private static void ClearStaticDictionary(System.Type owner, string fieldName)
        {
            var field = owner.GetField(fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field,
                $"{owner.Name} must have a private static field '{fieldName}' — rename detected?");
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

        // ---- Predicted-flag filter ---------------------------------------------

        [Test]
        public void Scan_ReplicatedFieldsWithoutPredictedFlag_AreOmitted()
        {
            // Only fields with Predicted = true should surface through
            // PredictionScanner; plain [Replicated] fields on the same aspect
            // must not leak into the prediction snapshot set.
            var fields = PredictionScanner.Scan(new MixedAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Predicted", fields[0].Field.Name);
        }

        [Test]
        public void Scan_OwnerAuthPredictedField_IsFilteredOut()
        {
            // ReplicationScanner strips the Predicted flag with a warning when
            // combined with Authority = Owner; the filter here must respect that
            // and produce an empty result. We run the scan and only assert on the
            // final shape — the warning is covered by ReplicationScannerTests.
            var capture = new SwallowingLogHandler();
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
            [Replicated(Predicted = true)] public ReactiveProperty<int> Zebra = new ReactiveProperty<int>(0);
            [Replicated(Predicted = true)] public ReactiveProperty<int> Alpha = new ReactiveProperty<int>(0);
        }

        private class CachingAspect
        {
            [Replicated(Predicted = true)] public ReactiveProperty<int> Value = new ReactiveProperty<int>(0);
        }

        private class BaseAspect
        {
            [Replicated(Predicted = true)] public ReactiveProperty<int> BaseValue = new ReactiveProperty<int>(0);
        }

        private class DerivedAspect : BaseAspect
        {
            [Replicated(Predicted = true)] public ReactiveProperty<float> DerivedValue = new ReactiveProperty<float>(0f);
        }

        private class ValueTypeAspect
        {
            [Replicated(Predicted = true)] public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class MixedAspect
        {
            [Replicated(Predicted = true)] public ReactiveProperty<int> Predicted = new ReactiveProperty<int>(0);
            [Replicated] public ReactiveProperty<int> PlainReplicated = new ReactiveProperty<int>(0);
        }

        private class NoMarkerAspect
        {
            public ReactiveProperty<int> Value = new ReactiveProperty<int>(0);
        }

        private class OwnerAuthPredictedAspect
        {
            [Replicated(Authority = AuthorityMode.Owner, Predicted = true)]
            public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        // ---- Log capture --------------------------------------------------------
        // Swallows logs from the ReplicationScanner-sourced Owner+Predicted warning
        // so it doesn't surface in the test runner Console. We don't assert on the
        // captured content here — that's the job of ReplicationScannerTests.
        private sealed class SwallowingLogHandler : ILogHandler
        {
            public void LogFormat(LogType logType, Object context, string format, params object[] args) { }
            public void LogException(System.Exception exception, Object context) { }
        }
    }
}
