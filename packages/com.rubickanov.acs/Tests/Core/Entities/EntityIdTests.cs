using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Tests for the <see cref="EntityId"/> value type itself. Behaviors in scope: the
    /// sentinel <see cref="EntityId.None"/>, monotonic uniqueness across <see cref="Entity"/>
    /// allocations, and struct equality. Generation is exercised indirectly via <c>new Entity()</c>
    /// because the raw Allocate() source is internal by design — callers don't mint ids themselves.
    /// </summary>
    [TestFixture]
    public class EntityIdTests
    {
        [Test]
        public void None_IsNone_ReturnsTrue()
        {
            Assert.IsTrue(EntityId.None.IsNone);
        }

        [Test]
        public void Default_IsNone_ReturnsTrue()
        {
            Assert.IsTrue(default(EntityId).IsNone,
                "default(EntityId) must coincide with None — tests that compare .Id != EntityId.None depend on this.");
        }

        [Test]
        public void NewEntityId_ZeroValue_IsNone()
        {
            var id = new EntityId(0);

            Assert.IsTrue(id.IsNone);
            Assert.AreEqual(EntityId.None, id);
        }

        [Test]
        public void AllocatedId_ViaEntity_IsNotNone()
        {
            var entity = new Entity();

            Assert.IsFalse(entity.Id.IsNone,
                "Every real entity must have a non-None id. If this fails, the by-id index is effectively a single-slot.");
        }

        [Test]
        public void TwoEntities_HaveDifferentIds()
        {
            var a = new Entity();
            var b = new Entity();

            Assert.AreNotEqual(a.Id, b.Id);
        }

        [Test]
        public void ManyEntities_AllHaveUniqueIds()
        {
            // Sanity check that the monotonic counter doesn't produce collisions across a batch.
            // Picking a small-but-nontrivial batch rather than a huge one keeps the test fast.
            var ids = new System.Collections.Generic.HashSet<EntityId>();
            for (int i = 0; i < 100; i++)
                Assert.IsTrue(ids.Add(new Entity().Id), "Allocate must return a value not already seen in this batch.");
        }

        [Test]
        public void Equality_SameValue_ReturnsTrue()
        {
            var a = new EntityId(42);
            var b = new EntityId(42);

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentValue_ReturnsFalse()
        {
            var a = new EntityId(42);
            var b = new EntityId(43);

            Assert.IsFalse(a.Equals(b));
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void ToString_None_MentionsNone()
        {
            StringAssert.Contains("None", EntityId.None.ToString());
        }

        [Test]
        public void ToString_NonNone_IncludesValue()
        {
            StringAssert.Contains("42", new EntityId(42).ToString());
        }
    }
}
