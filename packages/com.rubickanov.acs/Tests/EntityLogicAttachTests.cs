using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Verifies <see cref="EntityExtensions.AttachLogic"/> wires
    /// <see cref="IEntity.Destroyed"/> to <see cref="System.IDisposable.Dispose"/>
    /// correctly. Runs on a pure <see cref="Entity"/> — no Unity needed.
    /// </summary>
    [TestFixture]
    public class EntityLogicAttachTests
    {
        [Test]
        public void AttachLogic_OnDestroy_DisposesLogic()
        {
            var entity = new Entity();
            var logic = new CountingLogic();

            entity.AttachLogic(logic);
            Assert.AreEqual(0, logic.DisposeCount, "precondition: logic not disposed yet");

            entity.Dispose();

            Assert.AreEqual(1, logic.DisposeCount,
                "AttachLogic must release the logic via Destroyed when the entity is disposed.");
        }

        [Test]
        public void AttachLogic_ManualDispose_ThenEntityDispose_FrameworkStillFiresDisposeOnce()
        {
            // AttachLogic cannot observe a manual Dispose — the call goes
            // straight to the logic instance, bypassing the Destroyed hook.
            // The framework's invariant is narrower: it fires Dispose exactly
            // once through the Destroyed path. Implementations of IEntityLogic
            // are contractually required to be idempotent on Dispose (see doc),
            // so a second call is safe — CountingLogic here is intentionally
            // non-idempotent to prove the framework really fires exactly once.
            var entity = new Entity();
            var logic = new CountingLogic();

            entity.AttachLogic(logic);
            logic.Dispose();
            Assert.AreEqual(1, logic.DisposeCount);

            entity.Dispose();

            Assert.AreEqual(2, logic.DisposeCount,
                "AttachLogic must fire Dispose exactly once via Destroyed; combined with the manual call that's 2. A 3rd call would mean the handler stayed subscribed.");
        }

        [Test]
        public void AttachLogic_ReturnsPassedLogic_ForFluentChaining()
        {
            var entity = new Entity();
            var logic = new CountingLogic();

            var returned = entity.AttachLogic(logic);

            Assert.AreSame(logic, returned);
        }

        [Test]
        public void AttachLogic_MultipleLogics_AllDisposeOnDestroy()
        {
            var entity = new Entity();
            var a = new CountingLogic();
            var b = new CountingLogic();
            var c = new CountingLogic();

            entity.AttachLogic(a);
            entity.AttachLogic(b);
            entity.AttachLogic(c);

            entity.Dispose();

            Assert.AreEqual(1, a.DisposeCount);
            Assert.AreEqual(1, b.DisposeCount);
            Assert.AreEqual(1, c.DisposeCount);
        }

        private sealed class CountingLogic : IEntityLogic
        {
            public int DisposeCount;
            public void Dispose() => DisposeCount++;
        }
    }
}
