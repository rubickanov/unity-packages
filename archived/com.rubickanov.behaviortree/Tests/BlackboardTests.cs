using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BlackboardTests
    {
        private Blackboard _blackboard = default!;

        [SetUp]
        public void SetUp()
        {
            _blackboard = new Blackboard();
        }

        [Test]
        public void Set_ThenGet_Int_ReturnsSameValue()
        {
            var key = new BlackboardKey<int>("score");

            _blackboard.Set(key, 42);

            Assert.AreEqual(42, _blackboard.Get(key));
        }

        [Test]
        public void Set_ThenGet_String_ReturnsSameValue()
        {
            var key = new BlackboardKey<string>("name");

            _blackboard.Set(key, "alice");

            Assert.AreEqual("alice", _blackboard.Get(key));
        }

        [Test]
        public void Set_ThenGet_ReferenceType_ReturnsSameInstance()
        {
            var key = new BlackboardKey<object>("marker");
            var obj = new object();

            _blackboard.Set(key, obj);

            Assert.AreSame(obj, _blackboard.Get(key));
        }

        [Test]
        public void Get_MissingKey_ThrowsKeyNotFoundException()
        {
            var key = new BlackboardKey<int>("missing");

            Assert.Throws<KeyNotFoundException>(() => _blackboard.Get(key));
        }

        [Test]
        public void TryGet_MissingKey_ReturnsFalseAndDefault()
        {
            var key = new BlackboardKey<int>("missing");

            var ok = _blackboard.TryGet(key, out var value);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, value);
        }

        [Test]
        public void TryGet_ExistingKey_ReturnsTrueAndValue()
        {
            var key = new BlackboardKey<int>("count");
            _blackboard.Set(key, 7);

            var ok = _blackboard.TryGet(key, out var value);

            Assert.IsTrue(ok);
            Assert.AreEqual(7, value);
        }

        [Test]
        public void Has_BeforeSet_ReturnsFalse()
        {
            var key = new BlackboardKey<int>("flag");

            Assert.IsFalse(_blackboard.Has(key));
        }

        [Test]
        public void Has_AfterSet_ReturnsTrue()
        {
            var key = new BlackboardKey<int>("flag");
            _blackboard.Set(key, 1);

            Assert.IsTrue(_blackboard.Has(key));
        }

        [Test]
        public void Has_AfterRemove_ReturnsFalse()
        {
            var key = new BlackboardKey<int>("flag");
            _blackboard.Set(key, 1);

            _blackboard.Remove(key);

            Assert.IsFalse(_blackboard.Has(key));
        }

        [Test]
        public void Remove_NonExistentKey_DoesNotThrow()
        {
            var key = new BlackboardKey<int>("ghost");

            Assert.DoesNotThrow(() => _blackboard.Remove(key));
        }

        [Test]
        public void Set_SameKeyTwice_Overwrites()
        {
            var key = new BlackboardKey<int>("count");

            _blackboard.Set(key, 1);
            _blackboard.Set(key, 2);

            Assert.AreEqual(2, _blackboard.Get(key));
        }

        [Test]
        public void DifferentKeyInstances_WithSameName_AreTreatedAsDistinct()
        {
            // BlackboardKey is a reference type with no custom equality — two instances
            // sharing a name must NOT collide. Locks in the contract that users can pass
            // keys by identity, not by string matching.
            var keyA = new BlackboardKey<int>("shared");
            var keyB = new BlackboardKey<int>("shared");

            _blackboard.Set(keyA, 1);
            _blackboard.Set(keyB, 2);

            Assert.AreEqual(1, _blackboard.Get(keyA));
            Assert.AreEqual(2, _blackboard.Get(keyB));
        }
    }
}
