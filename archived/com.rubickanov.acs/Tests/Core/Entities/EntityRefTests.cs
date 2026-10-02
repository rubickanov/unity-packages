using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Tests for <see cref="EntityRef"/> — the domain wrapper used in aspect fields that point
    /// at another entity. Behaviors in scope: <see cref="EntityRef.None"/> semantics,
    /// struct equality by wrapped id, <see cref="EntityRef.From"/> null-safety, and resolve
    /// paths through a real <see cref="World"/> (success, None short-circuit, and dangling ref
    /// after the target entity is destroyed).
    /// </summary>
    [TestFixture]
    public class EntityRefTests
    {
        [Test]
        public void None_IsNone_ReturnsTrue()
        {
            Assert.IsTrue(EntityRef.None.IsNone);
        }

        [Test]
        public void Default_IsNone_ReturnsTrue()
        {
            Assert.IsTrue(default(EntityRef).IsNone,
                "default(EntityRef) must coincide with None — aspects that use `new ReactiveProperty<EntityRef>()` depend on this.");
        }

        [Test]
        public void NewEntityRef_FromEntityIdNone_IsNone()
        {
            var wrapped = new EntityRef(EntityId.None);

            Assert.IsTrue(wrapped.IsNone);
            Assert.AreEqual(EntityRef.None, wrapped);
        }

        [Test]
        public void NewEntityRef_FromNonNoneId_StoresId()
        {
            var id = new EntityId(42);

            var wrapped = new EntityRef(id);

            Assert.AreEqual(id, wrapped.Id);
            Assert.IsFalse(wrapped.IsNone);
        }

        [Test]
        public void From_RealEntity_CopiesEntityId()
        {
            var entity = new Entity();

            var wrapped = EntityRef.From(entity);

            Assert.AreEqual(entity.Id, wrapped.Id);
            Assert.IsFalse(wrapped.IsNone);
        }

        [Test]
        public void From_Null_ReturnsNone()
        {
            var wrapped = EntityRef.From(null);

            Assert.IsTrue(wrapped.IsNone,
                "EntityRef.From(null) must return None so callers can feed a possibly-missing IEntity without hand-rolled null checks.");
            Assert.AreEqual(EntityRef.None, wrapped);
        }

        [Test]
        public void Equality_SameId_ReturnsTrue()
        {
            var a = new EntityRef(new EntityId(7));
            var b = new EntityRef(new EntityId(7));

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentId_ReturnsFalse()
        {
            var a = new EntityRef(new EntityId(7));
            var b = new EntityRef(new EntityId(8));

            Assert.IsFalse(a.Equals(b));
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equals_ObjectOfDifferentType_ReturnsFalse()
        {
            var wrapped = new EntityRef(new EntityId(7));

            Assert.IsFalse(wrapped.Equals("not a ref"));
            Assert.IsFalse(wrapped.Equals(new EntityId(7)),
                "EntityRef and EntityId are not equal even when they carry the same numeric value — they are distinct types.");
        }

        [Test]
        public void TryResolve_None_ReturnsFalseAndNullEntity()
        {
            var world = new World();

            var ok = EntityRef.None.TryResolve(world, out var entity);

            Assert.IsFalse(ok);
            Assert.IsNull(entity);

            world.Dispose();
        }

        [Test]
        public void TryResolve_LiveEntity_ReturnsTrueAndSameInstance()
        {
            var world = new World();
            var entity = new Entity(world);
            var wrapped = EntityRef.From(entity);

            var ok = wrapped.TryResolve(world, out var resolved);

            Assert.IsTrue(ok);
            Assert.AreSame(entity, resolved);

            entity.Dispose();
            world.Dispose();
        }

        [Test]
        public void TryResolve_AfterEntityDisposed_ReturnsFalseAndNull()
        {
            var world = new World();
            var entity = new Entity(world);
            var wrapped = EntityRef.From(entity);
            entity.Dispose();

            var ok = wrapped.TryResolve(world, out var resolved);

            Assert.IsFalse(ok,
                "EntityRef must become dangling once the target entity is disposed — callers rely on this to drop stale AI targets.");
            Assert.IsNull(resolved);

            world.Dispose();
        }

        [Test]
        public void ResolveOrNull_LiveEntity_ReturnsInstance()
        {
            var world = new World();
            var entity = new Entity(world);
            var wrapped = EntityRef.From(entity);

            Assert.AreSame(entity, wrapped.ResolveOrNull(world));

            entity.Dispose();
            world.Dispose();
        }

        [Test]
        public void ResolveOrNull_None_ReturnsNull()
        {
            var world = new World();

            Assert.IsNull(EntityRef.None.ResolveOrNull(world));

            world.Dispose();
        }

        [Test]
        public void IsAlive_LiveEntity_ReturnsTrue()
        {
            var world = new World();
            var entity = new Entity(world);
            var wrapped = EntityRef.From(entity);

            Assert.IsTrue(wrapped.IsAlive(world));

            entity.Dispose();
            world.Dispose();
        }

        [Test]
        public void IsAlive_DisposedEntity_ReturnsFalse()
        {
            var world = new World();
            var entity = new Entity(world);
            var wrapped = EntityRef.From(entity);
            entity.Dispose();

            Assert.IsFalse(wrapped.IsAlive(world));

            world.Dispose();
        }

        [Test]
        public void IsAlive_None_ReturnsFalse()
        {
            var world = new World();

            Assert.IsFalse(EntityRef.None.IsAlive(world));

            world.Dispose();
        }

        [Test]
        public void ToString_None_MentionsNone()
        {
            StringAssert.Contains("None", EntityRef.None.ToString());
        }

        [Test]
        public void ToString_NonNone_IncludesId()
        {
            var text = new EntityRef(new EntityId(42)).ToString();

            StringAssert.Contains("42", text);
        }
    }
}
