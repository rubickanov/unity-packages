using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.StateMachine.Tests
{
    [TestFixture]
    public class CallbackStateTests
    {
        private enum Key { A, B }

        [Test]
        public void Lifecycle_AllCallbacksProvided_EachInvokedInItsPhase()
        {
            var enters = 0;
            var updates = 0;
            var exits = 0;
            var lastDelta = -1f;
            var state = new CallbackState(
                onEnter: () => enters++,
                onUpdate: dt => { updates++; lastDelta = dt; },
                onExit: () => exits++);

            state.OnEnter();
            state.OnUpdate(0.5f);
            state.OnExit();

            Assert.AreEqual(1, enters);
            Assert.AreEqual(1, updates);
            Assert.AreEqual(0.5f, lastDelta);
            Assert.AreEqual(1, exits);
        }

        [Test]
        public void Lifecycle_NullCallbacks_DoesNotThrow()
        {
            var state = new CallbackState();

            Assert.DoesNotThrow(() =>
            {
                state.OnEnter();
                state.OnUpdate(0.1f);
                state.OnExit();
            });
        }

        [Test]
        public void Lifecycle_OnlyUpdateCallback_OtherPhasesAreNoOps()
        {
            var updates = 0;
            var state = new CallbackState(onUpdate: _ => updates++);

            state.OnEnter();
            state.OnUpdate(0f);
            state.OnExit();

            Assert.AreEqual(1, updates);
        }

        [Test]
        public void AddState_Extension_RegistersCallbackStateAndReturnsFsm()
        {
            var fsm = new StateMachine<Key>();

            var result = fsm.AddState(Key.A, onEnter: () => { });

            Assert.AreSame(fsm, result);
            Assert.IsNotNull(fsm.GetState<CallbackState>(Key.A));
        }

        [Test]
        public void AddState_Extension_ChainedLambdas_DriveFullLifecycle()
        {
            var log = new List<string>();
            var fsm = new StateMachine<Key>();
            fsm
                .AddState(Key.A,
                    onEnter: () => log.Add("A:enter"),
                    onExit: () => log.Add("A:exit"))
                .AddState(Key.B,
                    onEnter: () => log.Add("B:enter"),
                    onUpdate: _ => log.Add("B:update"));

            fsm.Start(Key.A);
            fsm.SetState(Key.B);
            fsm.Update(0.016f);

            CollectionAssert.AreEqual(
                new[] { "A:enter", "A:exit", "B:enter", "B:update" },
                log);
        }
    }
}
