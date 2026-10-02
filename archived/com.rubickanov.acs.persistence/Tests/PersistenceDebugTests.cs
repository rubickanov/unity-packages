using System;
using System.Linq;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;

namespace Rubickanov.ACS.Tests.Persistence
{
    [TestFixture]
    public class PersistenceDebugTests
    {
        private enum Mood { Neutral, Happy }

        private sealed class CleanAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
            [PersistedState] public readonly ReactiveProperty<string> Name = new("");
        }

        private sealed class BrokenEnumAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<Mood> Mood = new(PersistenceDebugTests.Mood.Neutral);
        }

        [PersistedKey("debug.tests.keyed")]
        [PersistedVersion(3)]
        [PersistedAlias("debug.tests.old")]
        private sealed class KeyedAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Value = new(0);
        }

        [SetUp]
        public void SetUp()
        {
            // Other fixtures (e.g. PersistenceVersioningTests) seed a hand-built reverse index.
            // Reset here so ListPersistedKeys / FindKeyCollisions see the real assembly scan.
            PersistedKeyRegistry.ResetForTests();
        }

        [Test]
        public void ValidateAspect_CleanAspect_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => PersistenceDebug.ValidateAspect<CleanAspect>());
        }

        [Test]
        public void ValidateAspect_EnumWithoutAttribute_ThrowsWithFieldName()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PersistenceDebug.ValidateAspect<BrokenEnumAspect>());
            StringAssert.Contains("Mood", ex.Message);
            StringAssert.Contains("[PersistedEnum]", ex.Message);
        }

        [Test]
        public void ValidateAspect_NonAspect_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PersistenceDebug.ValidateAspect(typeof(string)));
        }

        [Test]
        public void ValidateAllAspects_TestAssemblyIncludesKnownBadAspect_ReturnsNonEmpty()
        {
            var errors = PersistenceDebug.ValidateAllAspects(typeof(BrokenEnumAspect).Assembly);

            // At minimum, our own BrokenEnumAspect surfaces — plus whatever other fixtures
            // declare. We don't assert exact count because sibling fixtures evolve.
            Assert.IsTrue(errors.Any(e => e.Contains("Mood") && e.Contains("[PersistedEnum]")),
                "ValidateAllAspects must pick up the broken enum field defined in this fixture.");
        }

        [Test]
        public void ListPersistedKeys_IncludesCustomKeyAndFullNameForKeyedAspect()
        {
            var entries = PersistenceDebug.ListPersistedKeys();

            Assert.IsTrue(entries.Any(e => e.Key == "debug.tests.keyed" && e.Type == typeof(KeyedAspect)),
                "Custom [PersistedKey] must show up in the dump.");
            Assert.IsTrue(entries.Any(e => e.Key == typeof(KeyedAspect).FullName && e.Type == typeof(KeyedAspect)),
                "Type.FullName entry must be registered up-front after reverse-index build.");
        }

        [Test]
        public void FindKeyCollisions_NoSeededCollisions_DoesNotReportKeyedAspect()
        {
            var collisions = PersistenceDebug.FindKeyCollisions();
            Assert.IsFalse(collisions.Any(c => c.Key == "debug.tests.keyed"),
                "A key claimed by a single aspect must not appear in the collision report.");
        }

        [Test]
        public void DumpAspect_IncludesFieldNamesAndVersion()
        {
            var dump = PersistenceDebug.DumpAspect(typeof(KeyedAspect));

            StringAssert.Contains("debug.tests.keyed", dump);
            StringAssert.Contains("Version: 3", dump);
            StringAssert.Contains("Value", dump);
            StringAssert.Contains("debug.tests.old", dump); // alias line
        }
    }
}
