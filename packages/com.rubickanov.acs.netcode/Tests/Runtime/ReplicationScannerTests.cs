using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class ReplicationScannerTests
    {
        // NOTE ON CACHE HYGIENE:
        // ReplicationScanner caches scan results per Type in a static dictionary that
        // lives for the whole test run. Each test therefore uses its own unique nested
        // type so no test observes a result cached by a previous test.

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

        // ---- Negative: unsupported value types ---------------------------------

        [Test]
        public void Scan_ReactivePropertyOfString_LogsErrorAndOmitsField()
        {
            LogAssert.Expect(LogType.Error, new Regex("not unmanaged"));

            var fields = ReplicationScanner.Scan(new InvalidStringStateAspect());

            Assert.AreEqual(0, fields.Length);
        }

        [Test]
        public void ScanEvents_SubjectOfManagedGeneric_LogsErrorAndOmitsEvent()
        {
            LogAssert.Expect(LogType.Error, new Regex("not unmanaged"));

            var events = ReplicationScanner.ScanEvents(new InvalidListEventAspect());

            Assert.AreEqual(0, events.Length);
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
            [ReplicatedState] public ReactiveProperty<int> Zebra = new ReactiveProperty<int>(0);
            [ReplicatedState] public ReactiveProperty<int> Alpha = new ReactiveProperty<int>(0);
        }

        private class CachingAspect
        {
            [ReplicatedState] public ReactiveProperty<int> Value = new ReactiveProperty<int>(0);
        }

        private class BaseAspect
        {
            [ReplicatedState] public ReactiveProperty<int> BaseValue = new ReactiveProperty<int>(0);
        }

        private class DerivedAspect : BaseAspect
        {
            [ReplicatedState] public ReactiveProperty<float> DerivedValue = new ReactiveProperty<float>(0f);
        }

        private class AttributedAspect
        {
            [ReplicatedState(Authority = AuthorityMode.Owner, Interpolation = InterpolationMode.Linear)]
            public ReactiveProperty<Vector3> Position = new ReactiveProperty<Vector3>(Vector3.zero);
        }

        private class InvalidStringStateAspect
        {
            // string is a managed reference type — must be rejected.
            [ReplicatedState] public ReactiveProperty<string> Bad = new ReactiveProperty<string>(string.Empty);
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
            [ReplicatedState] public ReactiveProperty<int> Cherry = new ReactiveProperty<int>(0);
            [ReplicatedState] public ReactiveProperty<int> Apple = new ReactiveProperty<int>(0);
            [ReplicatedState] public ReactiveProperty<int> Banana = new ReactiveProperty<int>(0);
        }
    }
}
