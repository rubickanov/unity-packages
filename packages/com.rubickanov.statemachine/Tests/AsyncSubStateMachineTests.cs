using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.StateMachine.Tests
{
    [TestFixture]
    public class AsyncSubStateMachineTests
    {
        private enum Parent { Menu, Combat, Paused }
        private enum Combat { Aiming, Firing, Reloading }

        private List<string> _log;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
        }

        private AsyncRecordingState NewState(string name) => new AsyncRecordingState(name, _log);

        [Test]
        public async Task OnEnterAsync_AsIAsyncState_StartsAtInitialState()
        {
            var sub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
            sub.AddState(Combat.Aiming, NewState("Aim"));
            sub.AddState(Combat.Firing, NewState("Fire"));

            await ((IAsyncState)sub).OnEnterAsync(CancellationToken.None);

            Assert.IsTrue(sub.IsStarted);
            Assert.AreEqual(Combat.Aiming, sub.CurrentKey);
            CollectionAssert.AreEqual(new[] { "Aim:Enter" }, _log);
        }

        [Test]
        public async Task OnUpdate_AsIAsyncState_TicksCurrentChildState()
        {
            var sub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
            sub.AddState(Combat.Aiming, NewState("Aim"));
            await ((IAsyncState)sub).OnEnterAsync(CancellationToken.None);
            _log.Clear();

            ((IAsyncState)sub).OnUpdate(0.1f);

            CollectionAssert.AreEqual(new[] { "Aim:Update" }, _log);
        }

        [Test]
        public async Task OnExitAsync_AsIAsyncState_StopsSubMachine()
        {
            var sub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
            sub.AddState(Combat.Aiming, NewState("Aim"));
            await ((IAsyncState)sub).OnEnterAsync(CancellationToken.None);

            await ((IAsyncState)sub).OnExitAsync(CancellationToken.None);

            Assert.IsFalse(sub.IsStarted);
            CollectionAssert.AreEqual(new[] { "Aim:Enter", "Aim:Exit" }, _log);
        }

        [Test]
        public async Task ParentTransition_IntoSub_StartsSubAtInitialState()
        {
            var parent = new AsyncStateMachine<Parent>();
            var combatSub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));
            combatSub.AddState(Combat.Firing, NewState("Fire"));

            parent.AddState(Parent.Menu, NewState("Menu"));
            parent.AddState(Parent.Combat, combatSub);
            await parent.StartAsync(Parent.Menu);
            _log.Clear();

            await parent.SetStateAsync(Parent.Combat);

            Assert.IsTrue(combatSub.IsStarted);
            Assert.AreEqual(Combat.Aiming, combatSub.CurrentKey);
            CollectionAssert.AreEqual(new[] { "Menu:Exit", "Aim:Enter" }, _log);
        }

        [Test]
        public async Task ParentTransition_OutOfSub_StopsChildBeforeNextParentEnter()
        {
            var parent = new AsyncStateMachine<Parent>();
            var combatSub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));

            parent.AddState(Parent.Menu, NewState("Menu"));
            parent.AddState(Parent.Combat, combatSub);
            await parent.StartAsync(Parent.Combat);
            _log.Clear();

            await parent.SetStateAsync(Parent.Menu);

            Assert.IsFalse(combatSub.IsStarted);
            CollectionAssert.AreEqual(new[] { "Aim:Exit", "Menu:Enter" }, _log);
        }

        [Test]
        public async Task SubMachine_Reentered_RestartsAtInitialState()
        {
            var parent = new AsyncStateMachine<Parent>();
            var combatSub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));
            combatSub.AddState(Combat.Firing, NewState("Fire"));

            parent.AddState(Parent.Menu, NewState("Menu"));
            parent.AddState(Parent.Combat, combatSub);
            await parent.StartAsync(Parent.Menu);

            await parent.SetStateAsync(Parent.Combat);
            await combatSub.SetStateAsync(Combat.Firing);
            await parent.SetStateAsync(Parent.Menu);
            _log.Clear();

            await parent.SetStateAsync(Parent.Combat);

            Assert.AreEqual(Combat.Aiming, combatSub.CurrentKey);
            CollectionAssert.AreEqual(new[] { "Menu:Exit", "Aim:Enter" }, _log);
        }

        [Test]
        public async Task ChildTransition_InsideSub_DoesNotFireParentStateChanged()
        {
            var parent = new AsyncStateMachine<Parent>();
            var combatSub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));
            combatSub.AddState(Combat.Firing, NewState("Fire"));

            parent.AddState(Parent.Combat, combatSub);
            await parent.StartAsync(Parent.Combat);

            var parentFireCount = 0;
            parent.StateChanged += (_, __) => parentFireCount++;

            await combatSub.SetStateAsync(Combat.Firing);

            Assert.AreEqual(0, parentFireCount);
            Assert.AreEqual(Combat.Firing, combatSub.CurrentKey);
        }

        private class AsyncRecordingState : AsyncStateBase
        {
            private readonly string _name;
            private readonly List<string> _log;

            public AsyncRecordingState(string name, List<string> log)
            {
                _name = name;
                _log = log;
            }

            public override UniTask OnEnterAsync(CancellationToken ct)
            {
                _log.Add(_name + ":Enter");
                return UniTask.CompletedTask;
            }

            public override void OnUpdate(float deltaTime) => _log.Add(_name + ":Update");

            public override UniTask OnExitAsync(CancellationToken ct)
            {
                _log.Add(_name + ":Exit");
                return UniTask.CompletedTask;
            }
        }
    }
}
