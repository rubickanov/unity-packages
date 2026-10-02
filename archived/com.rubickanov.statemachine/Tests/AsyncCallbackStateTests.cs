using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.StateMachine.Tests
{
    [TestFixture]
    public class AsyncCallbackStateTests
    {
        private enum Key { A, B }

        [Test]
        public async Task Lifecycle_AllCallbacksProvided_EachInvokedInItsPhase()
        {
            var enters = 0;
            var updates = 0;
            var exits = 0;
            var lastDelta = -1f;

            var state = new AsyncCallbackState(
                onEnterAsync: ct => { enters++; return UniTask.CompletedTask; },
                onUpdate: dt => { updates++; lastDelta = dt; },
                onExitAsync: ct => { exits++; return UniTask.CompletedTask; });

            await state.OnEnterAsync(CancellationToken.None);
            state.OnUpdate(0.5f);
            await state.OnExitAsync(CancellationToken.None);

            Assert.AreEqual(1, enters);
            Assert.AreEqual(1, updates);
            Assert.AreEqual(0.5f, lastDelta);
            Assert.AreEqual(1, exits);
        }

        [Test]
        public async Task Lifecycle_NullCallbacks_DoesNotThrow()
        {
            var state = new AsyncCallbackState();

            await state.OnEnterAsync(CancellationToken.None);
            state.OnUpdate(0.1f);
            await state.OnExitAsync(CancellationToken.None);
        }

        [Test]
        public async Task Lifecycle_OnlyUpdateCallback_OtherPhasesAreNoOps()
        {
            var updates = 0;
            var state = new AsyncCallbackState(onUpdate: _ => updates++);

            await state.OnEnterAsync(CancellationToken.None);
            state.OnUpdate(0f);
            await state.OnExitAsync(CancellationToken.None);

            Assert.AreEqual(1, updates);
        }

        [Test]
        public void AddState_Extension_RegistersAsyncCallbackStateAndReturnsFsm()
        {
            var fsm = new AsyncStateMachine<Key>();

            var result = fsm.AddState(Key.A, onEnterAsync: _ => UniTask.CompletedTask);

            Assert.AreSame(fsm, result);
            Assert.IsNotNull(fsm.GetState<AsyncCallbackState>(Key.A));
        }

        [Test]
        public async Task AddState_Extension_ChainedLambdas_DriveFullLifecycle()
        {
            var log = new List<string>();
            var fsm = new AsyncStateMachine<Key>();
            fsm
                .AddState(Key.A,
                    onEnterAsync: _ => { log.Add("A:enter"); return UniTask.CompletedTask; },
                    onExitAsync: _ => { log.Add("A:exit"); return UniTask.CompletedTask; })
                .AddState(Key.B,
                    onEnterAsync: _ => { log.Add("B:enter"); return UniTask.CompletedTask; },
                    onUpdate: _ => log.Add("B:update"));

            await fsm.StartAsync(Key.A);
            await fsm.SetStateAsync(Key.B);
            fsm.Update(0.016f);

            CollectionAssert.AreEqual(
                new[] { "A:enter", "A:exit", "B:enter", "B:update" },
                log);
        }
    }
}
