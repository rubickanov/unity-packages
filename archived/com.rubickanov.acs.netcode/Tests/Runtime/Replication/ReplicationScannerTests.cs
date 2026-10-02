using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ObservableCollections;
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
        public void ScanEvents_ReplicatedEventOnReactiveProperty_LogsErrorAndOmitsEvent()
        {
            // Symmetric to the state path: [ReplicatedEvent] on a non-Subject<T> field
            // is a programming error. Before #8 the event scanner silently dropped the
            // field and developers lost event wiring without any diagnostic.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedEventInfo[] events;
            try
            {
                events = ReplicationScanner.ScanEvents(new ReplicatedEventOnReactivePropertyAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, events.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("not a Subject<T>")),
                "Scanner must emit a LogError when [ReplicatedEvent] is attached to a ReactiveProperty field");
        }

        [Test]
        public void ScanEvents_ReplicatedEventOnPlainField_LogsErrorAndOmitsEvent()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedEventInfo[] events;
            try
            {
                events = ReplicationScanner.ScanEvents(new ReplicatedEventOnPlainFieldAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, events.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("not a Subject<T>")),
                "Scanner must emit a LogError when [ReplicatedEvent] is attached to a plain non-Subject field");
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

        // ---- Quantization mode propagation & validation -------------------------

        [Test]
        public void Scan_QuantizationNone_DefaultOnFieldInfo()
        {
            // No explicit Quantization in the attribute → ReplicatedFieldInfo.Quantization
            // must default to None so the factory routes to RawCodec<T> and existing
            // bindings keep their byte-exact wire format.
            var fields = ReplicationScanner.Scan(new QuantizationDefaultAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(QuantizationMode.None, fields[0].Quantization);
        }

        [Test]
        public void Scan_QuantizationHalfPrecisionOnVector3_CarriedOnFieldInfo()
        {
            var fields = ReplicationScanner.Scan(new QuantizationHalfVector3Aspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(QuantizationMode.HalfPrecision, fields[0].Quantization);
        }

        [Test]
        public void Scan_QuantizationOnEntityRef_LogsErrorAndOmitsField()
        {
            // EntityRef bypasses CodecRegistry — it goes through EntityRefCodec which
            // encodes NetworkObjectId, not the raw bytes. Silently ignoring Quantization
            // here would let authors write [Replicated(Quantization = HalfPrecision)] on
            // EntityRef and wonder why nothing happens. Scan must surface the mismatch.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new QuantizationOnEntityRefAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("EntityRef")),
                "Scanner must emit a LogError when Quantization is applied to an EntityRef field");
        }

        [Test]
        public void Scan_EntityRefNoQuantization_CarriedOnFieldInfo()
        {
            // Baseline: a plain EntityRef field must scan successfully so the factory can
            // construct an EntityRefCodec-backed binding in EntityReplicator.OnNetworkSpawn.
            var fields = ReplicationScanner.Scan(new PlainEntityRefAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(typeof(EntityRef), fields[0].ValueType);
            Assert.AreEqual(QuantizationMode.None, fields[0].Quantization);
        }

        [Test]
        public void Scan_QuantizationHalfPrecisionOnInt_LogsErrorAndOmitsField()
        {
            // HalfPrecision is not valid for int — surface the mismatch at scan time
            // rather than waiting for an InvalidOperationException at the first wire write.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new QuantizationInvalidIntAspect());
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Quantization")),
                "Scanner must emit a LogError when Quantization is invalid for the field type");
        }

        // ---- ObservableList collection replication -----------------------------

        [Test]
        public void Scan_ObservableListOfInt_Accepted_AndMarkedAsCollection()
        {
            // Before the collection replication phase this aspect was rejected with
            // "Collection delta-replication is not implemented yet". Now it must scan
            // cleanly and carry ReplicatedFieldKind.ObservableList so the factory picks
            // the correct binding.
            var fields = ReplicationScanner.Scan(new ObservableListIntAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Items", fields[0].Field.Name);
            Assert.AreEqual(ReplicatedFieldKind.ObservableList, fields[0].Kind);
            Assert.AreEqual(typeof(int), fields[0].ValueType);
        }

        [Test]
        public void Scan_ObservableListOfEntityRef_Accepted()
        {
            // EntityRef element type must go through EntityRefCodec, same as the scalar
            // path. Scan-time validation rejects Quantization on EntityRef — baseline
            // case here is plain [Replicated] with no quantization.
            var fields = ReplicationScanner.Scan(new ObservableListEntityRefAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(ReplicatedFieldKind.ObservableList, fields[0].Kind);
            Assert.AreEqual(typeof(EntityRef), fields[0].ValueType);
        }

        [Test]
        public void Scan_ObservableListOfString_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableListStringAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("not unmanaged")),
                "Scanner must reject ObservableList<string> because string is managed");
        }

        [Test]
        public void Scan_ObservableListWithInterpolationLinear_LogsErrorAndOmitsField()
        {
            // Collections have no meaningful lerp; flagging the combination at scan
            // time prevents the author from silently getting raw (non-interpolated)
            // behaviour on a field they explicitly annotated for interpolation.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableListLinearInterpAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Interpolation")),
                "Scanner must reject Interpolation = Linear on an ObservableList field");
        }

        [Test]
        public void Scan_ObservableListWithPredictedTrue_LogsErrorAndOmitsField()
        {
            // Prediction pipeline serialises fixed-layout snapshots. Collections don't
            // fit that contract in Phase 1; surface the mis-use rather than letting
            // predicted capture silently write nothing.
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableListPredictedAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Prediction")),
                "Scanner must reject Predicted = true on an ObservableList field");
        }

        // ---- ObservableDictionary collection replication -----------------------

        [Test]
        public void Scan_ObservableDictionaryOfStringFloat_Accepted_AndMarkedAsCollection()
        {
            // CooldownsAspect-style field: string key + unmanaged value. The scanner
            // must carry KeyType == string and ValueType == float so the factory can
            // build the StringKeyCodec + RawCodec<float> pair.
            var fields = ReplicationScanner.Scan(new ObservableDictionaryStringFloatAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Cooldowns", fields[0].Field.Name);
            Assert.AreEqual(ReplicatedFieldKind.ObservableDictionary, fields[0].Kind);
            Assert.AreEqual(typeof(string), fields[0].KeyType);
            Assert.AreEqual(typeof(float), fields[0].ValueType);
        }

        [Test]
        public void Scan_ObservableDictionaryOfIntInt_Accepted_ExercisesUnmanagedKeyPath()
        {
            // Unmanaged key — goes through UnmanagedKeyCodec<int> in the binding factory.
            var fields = ReplicationScanner.Scan(new ObservableDictionaryIntIntAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(ReplicatedFieldKind.ObservableDictionary, fields[0].Kind);
            Assert.AreEqual(typeof(int), fields[0].KeyType);
            Assert.AreEqual(typeof(int), fields[0].ValueType);
        }

        [Test]
        public void Scan_ObservableDictionaryOfIntEntityRef_Accepted()
        {
            // EntityRef value must route through EntityRefCodec, same as the list path.
            var fields = ReplicationScanner.Scan(new ObservableDictionaryEntityRefValueAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(ReplicatedFieldKind.ObservableDictionary, fields[0].Kind);
            Assert.AreEqual(typeof(int), fields[0].KeyType);
            Assert.AreEqual(typeof(EntityRef), fields[0].ValueType);
        }

        [Test]
        public void Scan_ObservableDictionaryOfManagedKey_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableDictionaryManagedKeyAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("key type is not unmanaged and not string")),
                "Scanner must reject ObservableDictionary with a managed non-string key");
        }

        [Test]
        public void Scan_ObservableDictionaryOfManagedValue_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableDictionaryManagedValueAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("value type is not unmanaged")),
                "Scanner must reject ObservableDictionary with a managed non-EntityRef value");
        }

        [Test]
        public void Scan_ObservableDictionaryWithInterpolationLinear_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableDictionaryLinearInterpAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Interpolation")),
                "Scanner must reject Interpolation = Linear on an ObservableDictionary field");
        }

        [Test]
        public void Scan_ObservableDictionaryWithPredictedTrue_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableDictionaryPredictedAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Prediction")),
                "Scanner must reject Predicted = true on an ObservableDictionary field");
        }

        [Test]
        public void Scan_ObservableDictionaryWithHalfPrecisionOnFloatValue_Accepted()
        {
            // Quantization applies to the VALUE type — HalfPrecision on float is valid.
            var fields = ReplicationScanner.Scan(new ObservableDictionaryHalfOnFloatAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(QuantizationMode.HalfPrecision, fields[0].Quantization);
            Assert.AreEqual(typeof(float), fields[0].ValueType);
        }

        [Test]
        public void Scan_ObservableDictionaryWithHalfPrecisionOnIntValue_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableDictionaryHalfOnIntAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Quantization")),
                "Scanner must reject HalfPrecision on ObservableDictionary<string,int> (int does not support quantization)");
        }

        // ---- ObservableHashSet collection replication --------------------------

        [Test]
        public void Scan_ObservableHashSetOfInt_Accepted_AndMarkedAsHashSetCollection()
        {
            var fields = ReplicationScanner.Scan(new ObservableHashSetIntAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Tags", fields[0].Field.Name);
            Assert.AreEqual(ReplicatedFieldKind.ObservableHashSet, fields[0].Kind);
            Assert.AreEqual(typeof(int), fields[0].ValueType);
        }

        [Test]
        public void Scan_ObservableHashSetOfString_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableHashSetStringAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("not unmanaged")),
                "Scanner must reject ObservableHashSet<string> because string is managed");
        }

        [Test]
        public void Scan_ObservableHashSetWithInterpolationLinear_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableHashSetLinearInterpAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Interpolation")),
                "Scanner must reject Interpolation = Linear on an ObservableHashSet field");
        }

        [Test]
        public void Scan_ObservableHashSetWithPredictedTrue_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableHashSetPredictedAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Prediction")),
                "Scanner must reject Predicted = true on an ObservableHashSet field");
        }

        // ---- ObservableFixedSizeRingBuffer collection replication --------------

        [Test]
        public void Scan_ObservableFixedSizeRingBufferOfInt_Accepted_AndMarkedAsRingBufferCollection()
        {
            var fields = ReplicationScanner.Scan(new ObservableRingBufferIntAspect());

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Log", fields[0].Field.Name);
            Assert.AreEqual(ReplicatedFieldKind.ObservableRingBuffer, fields[0].Kind);
            Assert.AreEqual(typeof(int), fields[0].ValueType);
        }

        [Test]
        public void Scan_UnboundedObservableRingBuffer_LogsTargetedErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableRingBufferUnboundedIntAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("ObservableFixedSizeRingBuffer")),
                "Scanner must reject plain ObservableRingBuffer<T> with a message naming the fixed-size alternative");
        }

        [Test]
        public void Scan_ObservableRingBufferWithInterpolationLinear_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableRingBufferLinearInterpAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Interpolation")),
                "Scanner must reject Interpolation = Linear on an ObservableFixedSizeRingBuffer field");
        }

        [Test]
        public void Scan_ObservableRingBufferWithPredictedTrue_LogsErrorAndOmitsField()
        {
            var capture = new CapturingLogHandler();
            var original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            ReplicatedFieldInfo[] fields;
            try
            {
                fields = ReplicationScanner.Scan(new ObservableRingBufferPredictedAspect());
            }
            finally { Debug.unityLogger.logHandler = original; }

            Assert.AreEqual(0, fields.Length);
            Assert.IsTrue(
                capture.Captured.Any(e => e.type == LogType.Error && e.message.Contains("Prediction")),
                "Scanner must reject Predicted = true on an ObservableFixedSizeRingBuffer field");
        }

        // ---- Test aspects -------------------------------------------------------
        // One per test to avoid static-cache contamination.

        private class QuantizationDefaultAspect
        {
            [Replicated] public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class QuantizationHalfVector3Aspect
        {
            [Replicated(Quantization = QuantizationMode.HalfPrecision)]
            public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class QuantizationInvalidIntAspect
        {
            [Replicated(Quantization = QuantizationMode.HalfPrecision)]
            public ReactiveProperty<int> Bad = new ReactiveProperty<int>(0);
        }

        private class QuantizationOnEntityRefAspect
        {
            [Replicated(Quantization = QuantizationMode.HalfPrecision)]
            public ReactiveProperty<EntityRef> Bad = new ReactiveProperty<EntityRef>(EntityRef.None);
        }

        private class PlainEntityRefAspect
        {
            [Replicated] public ReactiveProperty<EntityRef> Target = new ReactiveProperty<EntityRef>(EntityRef.None);
        }

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

        private class ReplicatedEventOnReactivePropertyAspect
        {
            // [ReplicatedEvent] expects a Subject<T>; a ReactiveProperty<T> must be rejected.
            [ReplicatedEvent] public ReactiveProperty<int> Bad = new ReactiveProperty<int>(0);
        }

        private class ReplicatedEventOnPlainFieldAspect
        {
#pragma warning disable CS0649
            [ReplicatedEvent] public int Bad;
#pragma warning restore CS0649
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

        // ---- Collection-field test aspects -------------------------------------

        private class ObservableListIntAspect
        {
            [Replicated] public ObservableList<int> Items = new ObservableList<int>();
        }

        private class ObservableListEntityRefAspect
        {
            [Replicated] public ObservableList<EntityRef> Targets = new ObservableList<EntityRef>();
        }

        private class ObservableListStringAspect
        {
            [Replicated] public ObservableList<string> BadTags = new ObservableList<string>();
        }

        private class ObservableListLinearInterpAspect
        {
            [Replicated(Interpolation = InterpolationMode.Linear)]
            public ObservableList<int> Items = new ObservableList<int>();
        }

        private class ObservableListPredictedAspect
        {
            [Replicated(Predicted = true)]
            public ObservableList<int> Items = new ObservableList<int>();
        }

        private class ObservableDictionaryStringFloatAspect
        {
            [Replicated] public ObservableDictionary<string, float> Cooldowns = new ObservableDictionary<string, float>();
        }

        private class ObservableDictionaryIntIntAspect
        {
            [Replicated] public ObservableDictionary<int, int> Map = new ObservableDictionary<int, int>();
        }

        private class ObservableDictionaryEntityRefValueAspect
        {
            [Replicated] public ObservableDictionary<int, EntityRef> Targets = new ObservableDictionary<int, EntityRef>();
        }

        private class ObservableDictionaryManagedKeyAspect
        {
            // List<int> is a managed reference type — invalid as a dictionary key.
            [Replicated] public ObservableDictionary<List<int>, int> Bad = new ObservableDictionary<List<int>, int>();
        }

        private class ObservableDictionaryManagedValueAspect
        {
            // string value is managed and not EntityRef — invalid.
            [Replicated] public ObservableDictionary<int, string> Bad = new ObservableDictionary<int, string>();
        }

        private class ObservableDictionaryLinearInterpAspect
        {
            [Replicated(Interpolation = InterpolationMode.Linear)]
            public ObservableDictionary<string, int> Bad = new ObservableDictionary<string, int>();
        }

        private class ObservableDictionaryPredictedAspect
        {
            [Replicated(Predicted = true)]
            public ObservableDictionary<string, int> Bad = new ObservableDictionary<string, int>();
        }

        private class ObservableDictionaryHalfOnFloatAspect
        {
            [Replicated(Quantization = QuantizationMode.HalfPrecision)]
            public ObservableDictionary<string, float> Cooldowns = new ObservableDictionary<string, float>();
        }

        private class ObservableDictionaryHalfOnIntAspect
        {
            [Replicated(Quantization = QuantizationMode.HalfPrecision)]
            public ObservableDictionary<string, int> Bad = new ObservableDictionary<string, int>();
        }

        // ---- ObservableHashSet test aspects ------------------------------------

        private class ObservableHashSetIntAspect
        {
            [Replicated] public ObservableHashSet<int> Tags = new ObservableHashSet<int>();
        }

        private class ObservableHashSetStringAspect
        {
            [Replicated] public ObservableHashSet<string> BadTags = new ObservableHashSet<string>();
        }

        private class ObservableHashSetLinearInterpAspect
        {
            [Replicated(Interpolation = InterpolationMode.Linear)]
            public ObservableHashSet<int> Tags = new ObservableHashSet<int>();
        }

        private class ObservableHashSetPredictedAspect
        {
            [Replicated(Predicted = true)]
            public ObservableHashSet<int> Tags = new ObservableHashSet<int>();
        }

        // ---- ObservableFixedSizeRingBuffer test aspects ------------------------

        private class ObservableRingBufferIntAspect
        {
            [Replicated] public ObservableFixedSizeRingBuffer<int> Log = new ObservableFixedSizeRingBuffer<int>(capacity: 8);
        }

        private class ObservableRingBufferUnboundedIntAspect
        {
            // Plain unbounded ObservableRingBuffer<T> must be rejected with a
            // targeted error naming the supported alternative.
            [Replicated] public ObservableRingBuffer<int> Log = new ObservableRingBuffer<int>();
        }

        private class ObservableRingBufferLinearInterpAspect
        {
            [Replicated(Interpolation = InterpolationMode.Linear)]
            public ObservableFixedSizeRingBuffer<int> Log = new ObservableFixedSizeRingBuffer<int>(capacity: 8);
        }

        private class ObservableRingBufferPredictedAspect
        {
            [Replicated(Predicted = true)]
            public ObservableFixedSizeRingBuffer<int> Log = new ObservableFixedSizeRingBuffer<int>(capacity: 8);
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
