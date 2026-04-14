using System.Linq;
using NUnit.Framework;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class PersistenceScannerTests
    {
        private sealed class PlainAspect : IEntityAspect
        {
            public readonly ReactiveProperty<int> Untagged = new(0);
        }

        private sealed class OneFieldAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
            public readonly ReactiveProperty<bool> IsInCombat = new(false);
        }

        private sealed class StringFieldAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<string> Name = new("unset");
        }

        private sealed class ReferenceTypeAspect : IEntityAspect
        {
            // Wrapping a reference type in ReactiveProperty is against project convention.
            // Scanner must fail-fast with a logged error.
            [PersistedState] public readonly ReactiveProperty<object> Bad = new(null);
        }

        private sealed class CollectionsAspect : IEntityAspect
        {
            [PersistedState] public readonly ObservableList<int> List = new();
            [PersistedState] public readonly ObservableDictionary<string, float> Dict = new();
            [PersistedState] public readonly ObservableHashSet<int> Set = new();
        }

        private sealed class ReverseOrderAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Zulu = new(0);
            [PersistedState] public readonly ReactiveProperty<int> Mike = new(0);
            [PersistedState] public readonly ReactiveProperty<int> Alpha = new(0);
        }

        private class BaseAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> BaseField = new(0);
        }

        private sealed class DerivedAspect : BaseAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> DerivedField = new(0);
        }

        [Test]
        public void Scan_AspectWithoutPersistedField_ReturnsEmpty()
        {
            var aspect = new PlainAspect();

            var fields = PersistenceScanner.Scan(aspect);

            Assert.IsEmpty(fields);
        }

        [Test]
        public void Scan_AspectWithOnePersistedField_ReturnsOneEntry()
        {
            var aspect = new OneFieldAspect();

            var fields = PersistenceScanner.Scan(aspect);

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(nameof(OneFieldAspect.Health), fields[0].Field.Name);
        }

        [Test]
        public void Scan_StringField_IsAllowed()
        {
            var aspect = new StringFieldAspect();

            var fields = PersistenceScanner.Scan(aspect);

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(typeof(string), fields[0].ValueType);
        }

        [Test]
        public void Scan_ReferenceTypeReactiveProperty_LogsErrorAndSkips()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("PersistenceScanner.*Bad"));

            var aspect = new ReferenceTypeAspect();
            var fields = PersistenceScanner.Scan(aspect);

            Assert.IsEmpty(fields);
        }

        [Test]
        public void Scan_Collections_AllThreeKindsDetected()
        {
            var aspect = new CollectionsAspect();

            var fields = PersistenceScanner.Scan(aspect);

            var kinds = fields.Select(f => f.Kind).ToArray();
            CollectionAssert.Contains(kinds, PersistedFieldKind.ObservableList);
            CollectionAssert.Contains(kinds, PersistedFieldKind.ObservableDictionary);
            CollectionAssert.Contains(kinds, PersistedFieldKind.ObservableHashSet);
        }

        [Test]
        public void Scan_FieldsDeclaredInReverseAlphabeticalOrder_ReturnedAlphabetically()
        {
            var aspect = new ReverseOrderAspect();

            var names = PersistenceScanner.Scan(aspect).Select(f => f.Field.Name).ToArray();

            CollectionAssert.AreEqual(new[] { "Alpha", "Mike", "Zulu" }, names);
        }

        [Test]
        public void Scan_DerivedAspect_IncludesBaseAndDerivedFields()
        {
            var aspect = new DerivedAspect();

            var names = PersistenceScanner.Scan(aspect).Select(f => f.Field.Name).ToArray();

            CollectionAssert.Contains(names, "BaseField");
            CollectionAssert.Contains(names, "DerivedField");
        }

        [Test]
        public void HasPersistedFields_AspectWithoutPersistedField_ReturnsFalse()
        {
            var aspect = new PlainAspect();

            Assert.IsFalse(PersistenceScanner.HasPersistedFields(aspect));
        }

        [Test]
        public void HasPersistedFields_AspectWithPersistedField_ReturnsTrue()
        {
            var aspect = new OneFieldAspect();

            Assert.IsTrue(PersistenceScanner.HasPersistedFields(aspect));
        }
    }
}
